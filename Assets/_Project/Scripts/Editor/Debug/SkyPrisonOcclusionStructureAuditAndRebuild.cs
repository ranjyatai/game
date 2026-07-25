using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-pass source audit for terrain decoration 2.5D occlusion structure.
/// It distinguishes four cases:
/// 1) BackTrigger node missing.
/// 2) BackTrigger has Missing MonoBehaviour slots.
/// 3) BackTrigger lacks a valid SkyPrisonTerrainDecorationFrontOccluderTrigger.
/// 4) Instance was not rebuilt by the current builder/stamp.
/// </summary>
public static class SkyPrisonOcclusionStructureAuditAndRebuild_V1
{
    private const string MenuRoot = "Tools/Sky Prison/Debug/Occlusion Structure Audit/";
    private const string VisualRootName = "VisualRoot";
    private const string RuleRootName = "RuleRoot";
    private const string CollisionRootName = "CollisionRoot";
    private const string BackTriggerName = "BackTrigger";
    private const string FrontTriggerName = "FrontTrigger";
    private const string FrontOccluderRootName = "FrontOccluderRoot";

    private struct AuditRow
    {
        public GameObject root;
        public TerrainDecorationRuntimeBinder binder;
        public TerrainDecorationDefinition definition;
        public Transform visualRoot;
        public Transform ruleRoot;
        public Transform collisionRoot;
        public Transform backTrigger;
        public Transform frontTrigger;
        public Transform frontOccluderRoot;
        public SkyPrisonTerrainDecorationFrontOccluderTrigger trigger;
        public SkyPrisonTerrainDecorationStructureStamp stamp;
        public int missingOnBackTrigger;
        public string scriptVersion;
        public string path;

        public bool HasFatalProblem => backTrigger == null || missingOnBackTrigger > 0 || trigger == null;
        public bool NeedsBuilderRebuild => binder != null && definition != null && HasFatalProblem;
    }

    [MenuItem(MenuRoot + "1. Audit Active Scene", priority = 10)]
    public static void AuditActiveScene()
    {
        List<AuditRow> rows = CollectSceneRows(includeInactive: true);
        Debug.Log(BuildReport("ACTIVE SCENE", rows, includeDetails: true));
    }

    [MenuItem(MenuRoot + "2. Rebuild Selected Decoration Roots Through Builder", priority = 20)]
    public static void RebuildSelectedThroughBuilder()
    {
        int rebuilt = 0;
        foreach (GameObject selected in Selection.gameObjects)
        {
            TerrainDecorationRuntimeBinder binder = selected.GetComponentInParent<TerrainDecorationRuntimeBinder>();
            if (binder == null || binder.definition == null)
            {
                Debug.LogWarning($"[OcclusionStructureAudit] Skip selected={GetPath(selected != null ? selected.transform : null)} because TerrainDecorationRuntimeBinder/definition is missing.", selected);
                continue;
            }

            Undo.RegisterFullObjectHierarchyUndo(binder.gameObject, "Rebuild terrain decoration occlusion structure");
            SkyPrisonTerrainDecorationInstanceBuilder.BuildStructureFromDefinition(binder.gameObject, binder.definition, true);
            EditorUtility.SetDirty(binder.gameObject);
            rebuilt++;
        }

        if (rebuilt > 0)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[OcclusionStructureAudit] Rebuilt selected decoration root(s): {rebuilt}");
        AuditActiveScene();
    }

    [MenuItem(MenuRoot + "3. Rebuild All Scene Decoration Roots Through Builder", priority = 30)]
    public static void RebuildAllSceneThroughBuilder()
    {
        TerrainDecorationRuntimeBinder[] binders = Object.FindObjectsOfType<TerrainDecorationRuntimeBinder>(true);
        int rebuilt = 0;
        int skipped = 0;

        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null || binder.definition == null)
            {
                skipped++;
                continue;
            }

            Undo.RegisterFullObjectHierarchyUndo(binder.gameObject, "Rebuild all terrain decoration occlusion structures");
            SkyPrisonTerrainDecorationInstanceBuilder.BuildStructureFromDefinition(binder.gameObject, binder.definition, false);
            EditorUtility.SetDirty(binder.gameObject);
            rebuilt++;
        }

        if (rebuilt > 0)
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"[OcclusionStructureAudit] Rebuilt scene decoration root(s): {rebuilt}, skipped={skipped}");
        AuditActiveScene();
    }

    [MenuItem(MenuRoot + "4. Audit Prefabs Under Assets/_Project", priority = 40)]
    public static void AuditProjectPrefabs()
    {
        List<AuditRow> all = new List<AuditRow>();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" });
        int prefabCount = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            TerrainDecorationRuntimeBinder binder = prefab.GetComponentInChildren<TerrainDecorationRuntimeBinder>(true);
            Transform back = FindChildRecursive(prefab.transform, BackTriggerName);
            if (binder == null && back == null)
                continue;

            prefabCount++;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                List<AuditRow> rows = CollectRowsUnderRoot(contents, includeInactive: true);
                for (int i = 0; i < rows.Count; i++)
                {
                    AuditRow row = rows[i];
                    row.path = path + " :: " + row.path;
                    all.Add(row);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        Debug.Log(BuildReport($"PROJECT PREFABS scanned={prefabCount}", all, includeDetails: true));
    }

    [MenuItem(MenuRoot + "5. Rebuild Prefabs Under Assets/_Project Through Builder", priority = 50)]
    public static void RebuildProjectPrefabsThroughBuilder()
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project" });
        int touched = 0;
        int rebuilt = 0;
        int skipped = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            if (prefab.GetComponentInChildren<TerrainDecorationRuntimeBinder>(true) == null && FindChildRecursive(prefab.transform, BackTriggerName) == null)
                continue;

            touched++;
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            bool dirty = false;
            try
            {
                TerrainDecorationRuntimeBinder[] binders = contents.GetComponentsInChildren<TerrainDecorationRuntimeBinder>(true);
                foreach (TerrainDecorationRuntimeBinder binder in binders)
                {
                    if (binder == null || binder.definition == null)
                    {
                        skipped++;
                        continue;
                    }

                    SkyPrisonTerrainDecorationInstanceBuilder.BuildStructureFromDefinition(binder.gameObject, binder.definition, false);
                    rebuilt++;
                    dirty = true;
                }

                if (dirty)
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[OcclusionStructureAudit] Prefab rebuild complete. touched={touched}, rebuiltRoot={rebuilt}, skipped={skipped}");
    }

    private static List<AuditRow> CollectSceneRows(bool includeInactive)
    {
        List<AuditRow> rows = new List<AuditRow>();
        TerrainDecorationRuntimeBinder[] binders = Object.FindObjectsOfType<TerrainDecorationRuntimeBinder>(includeInactive);
        HashSet<GameObject> visited = new HashSet<GameObject>();

        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null || binder.gameObject == null)
                continue;
            visited.Add(binder.gameObject);
            rows.Add(BuildRow(binder.gameObject, binder));
        }

        // Catch orphan structures with BackTrigger but no binder.
        GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (GameObject go in all)
        {
            if (go == null || !go.scene.IsValid())
                continue;
            if (go.name != BackTriggerName)
                continue;

            Transform root = FindDecorationRootFromBackTrigger(go.transform);
            if (root == null || visited.Contains(root.gameObject))
                continue;

            visited.Add(root.gameObject);
            rows.Add(BuildRow(root.gameObject, root.GetComponent<TerrainDecorationRuntimeBinder>()));
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
        return rows;
    }

    private static List<AuditRow> CollectRowsUnderRoot(GameObject root, bool includeInactive)
    {
        List<AuditRow> rows = new List<AuditRow>();
        if (root == null)
            return rows;

        TerrainDecorationRuntimeBinder[] binders = root.GetComponentsInChildren<TerrainDecorationRuntimeBinder>(includeInactive);
        HashSet<GameObject> visited = new HashSet<GameObject>();
        foreach (TerrainDecorationRuntimeBinder binder in binders)
        {
            if (binder == null)
                continue;
            visited.Add(binder.gameObject);
            rows.Add(BuildRow(binder.gameObject, binder));
        }

        foreach (Transform t in root.GetComponentsInChildren<Transform>(includeInactive))
        {
            if (t == null || t.name != BackTriggerName)
                continue;
            Transform decoRoot = FindDecorationRootFromBackTrigger(t);
            if (decoRoot == null || visited.Contains(decoRoot.gameObject))
                continue;
            visited.Add(decoRoot.gameObject);
            rows.Add(BuildRow(decoRoot.gameObject, decoRoot.GetComponent<TerrainDecorationRuntimeBinder>()));
        }

        rows.Sort((a, b) => string.CompareOrdinal(a.path, b.path));
        return rows;
    }

    private static AuditRow BuildRow(GameObject root, TerrainDecorationRuntimeBinder binder)
    {
        Transform rootT = root != null ? root.transform : null;
        Transform visual = rootT != null ? rootT.Find(VisualRootName) : null;
        Transform rule = rootT != null ? rootT.Find(RuleRootName) : null;
        Transform collision = rootT != null ? rootT.Find(CollisionRootName) : null;
        Transform back = rule != null ? rule.Find(BackTriggerName) : null;
        Transform front = rule != null ? rule.Find(FrontTriggerName) : null;
        Transform frontRoot = rule != null ? rule.Find(FrontOccluderRootName) : null;
        SkyPrisonTerrainDecorationFrontOccluderTrigger trigger = back != null ? back.GetComponent<SkyPrisonTerrainDecorationFrontOccluderTrigger>() : null;

        return new AuditRow
        {
            root = root,
            binder = binder,
            definition = binder != null ? binder.definition : null,
            visualRoot = visual,
            ruleRoot = rule,
            collisionRoot = collision,
            backTrigger = back,
            frontTrigger = front,
            frontOccluderRoot = frontRoot,
            trigger = trigger,
            stamp = root != null ? root.GetComponent<SkyPrisonTerrainDecorationStructureStamp>() : null,
            missingOnBackTrigger = back != null ? GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(back.gameObject) : 0,
            scriptVersion = trigger != null ? ReadSerializedString(trigger, "scriptVersion") : "<none>",
            path = GetPath(rootT),
        };
    }

    private static string BuildReport(string title, List<AuditRow> rows, bool includeDetails)
    {
        int roots = rows.Count;
        int missingBack = 0;
        int missingScripts = 0;
        int triggerMissing = 0;
        int noDefinition = 0;
        int noStamp = 0;
        int fatal = 0;

        StringBuilder sb = new StringBuilder(16384);
        sb.AppendLine($"[OcclusionStructureAudit] {title}");

        foreach (AuditRow row in rows)
        {
            if (row.backTrigger == null) missingBack++;
            if (row.missingOnBackTrigger > 0) missingScripts += row.missingOnBackTrigger;
            if (row.trigger == null) triggerMissing++;
            if (row.definition == null) noDefinition++;
            if (row.stamp == null) noStamp++;
            if (row.HasFatalProblem) fatal++;
        }

        sb.AppendLine($"roots={roots} fatal={fatal} missingBackTrigger={missingBack} missingScriptSlots={missingScripts} noValidTrigger={triggerMissing} noDefinition={noDefinition} noStamp={noStamp}");

        if (!includeDetails)
            return sb.ToString();

        for (int i = 0; i < rows.Count; i++)
        {
            AuditRow r = rows[i];
            string status = r.HasFatalProblem ? "BROKEN" : "OK";
            sb.AppendLine($"[{i}] {status} {r.path}");
            sb.AppendLine($"    definition={(r.definition != null ? r.definition.name : "<null>")} stamp={(r.stamp != null ? r.stamp.BuilderVersion : "<none>")}");
            sb.AppendLine($"    VisualRoot={BoolName(r.visualRoot)} RuleRoot={BoolName(r.ruleRoot)} CollisionRoot={BoolName(r.collisionRoot)}");
            sb.AppendLine($"    BackTrigger={BoolName(r.backTrigger)} MissingScripts={r.missingOnBackTrigger} Trigger={(r.trigger != null ? r.trigger.GetType().Name : "<null>")} Version={r.scriptVersion}");
            sb.AppendLine($"    FrontTrigger={BoolName(r.frontTrigger)} FrontOccluderRoot={BoolName(r.frontOccluderRoot)}");
        }

        return sb.ToString();
    }

    private static string BoolName(Object obj) => obj != null ? "OK" : "MISSING";

    private static string ReadSerializedString(Object target, string propertyName)
    {
        if (target == null)
            return "";
        SerializedObject so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(propertyName);
        return prop != null && prop.propertyType == SerializedPropertyType.String ? prop.stringValue : "";
    }

    private static Transform FindDecorationRootFromBackTrigger(Transform backTrigger)
    {
        if (backTrigger == null)
            return null;

        Transform rule = backTrigger.parent;
        if (rule == null || rule.name != RuleRootName)
            return null;

        return rule.parent;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        foreach (Transform child in root)
        {
            Transform found = FindChildRecursive(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "<null>";
        Stack<string> stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack.ToArray());
    }
}
