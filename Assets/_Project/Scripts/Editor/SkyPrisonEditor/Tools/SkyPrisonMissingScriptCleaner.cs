#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class SkyPrisonMissingScriptCleaner
{
    [MenuItem("Tools/Sky Prison/Units/Clean Missing Scripts In Selected Prefabs")]
    public static void CleanSelectedPrefabs()
    {
        Object[] selected = Selection.objects;
        int prefabCount = 0;
        int removedTotal = 0;

        foreach (Object obj in selected)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
                continue;

            int removed = CleanPrefabAtPath(path);
            if (removed >= 0)
            {
                prefabCount++;
                removedTotal += removed;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SkyPrisonMissingScriptCleaner] Cleaned {prefabCount} prefab(s), removed {removedTotal} missing script component(s).");
    }

    [MenuItem("Tools/Sky Prison/Units/Clean Missing Scripts In Selected Prefabs", true)]
    public static bool ValidateCleanSelectedPrefabs()
    {
        foreach (Object obj in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab"))
                return true;
        }
        return false;
    }

    [MenuItem("Tools/Sky Prison/Units/Clean Missing Scripts In All Unit Prefabs")]
    public static void CleanAllUnitPrefabs()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Data/Units" });
        int prefabCount = 0;
        int removedTotal = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            int removed = CleanPrefabAtPath(path);
            if (removed >= 0)
            {
                prefabCount++;
                removedTotal += removed;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SkyPrisonMissingScriptCleaner] Cleaned {prefabCount} unit prefab(s), removed {removedTotal} missing script component(s).");
    }

    public static int CleanPrefabAtPath(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath) || !prefabPath.EndsWith(".prefab"))
            return -1;

        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            return -1;

        int removed = 0;
        try
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                if (count <= 0)
                    continue;

                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                removed += count;
                Debug.Log($"[SkyPrisonMissingScriptCleaner] Removed {count} missing script(s): {prefabPath} / {GetPath(t)}");
            }

            if (removed > 0)
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        return removed;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return string.Empty;

        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
#endif
