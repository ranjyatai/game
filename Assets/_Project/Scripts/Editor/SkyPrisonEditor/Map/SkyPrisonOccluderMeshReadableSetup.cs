using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 让参与前后遮挡的装饰物模型 Read/Write 可读，并撤掉之前误加的遮挡探测碰撞体。
///
/// 前后遮挡的自判定走的是 Mesh 三角面（useSelfVisualMeshTriangleRayDepth，默认开启），
/// 不是物理碰撞体。而三角面路径要求 Mesh 可读：
///   Mesh 不可读 → 跳过三角面 → 回退 Renderer Bounds → 那条已标记废弃、不参与判定
///   → 三条路径全空 → 永远判定不遮挡
/// SGS_Grass.fbx 的 isReadable 就是 0，这是草不遮挡的根因。
///
/// 之前我给这类装饰物加了 Box 探测碰撞体想给射线一个靶子，方向是错的：
/// 遮挡从来不靠物理形状。而且那个 Box 落在 World3D 层（Builder 有一道把 VisualRoot
/// 整棵子树刷成 World3D 的递归，排在生成之后），World3D 在 blockingLayers 里，
/// 结果是草开始挡人。这里一并关掉。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonOccluderMeshReadableSetup
{
    private const string LogPrefix = "[SkyPrison OccluderMesh]";
    private const string VersionKey = "SkyPrison.OccluderMeshReadable.Version";
    private const int Version = 1;

    static SkyPrisonOccluderMeshReadableSetup()
    {
        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce()
    {
        if (EditorPrefs.GetInt(VersionKey, 0) >= Version)
            return;

        EditorPrefs.SetInt(VersionKey, Version);
        Run();
    }

    [MenuItem("Tools/Sky Prison/Map/前景遮挡/让遮挡模型可读并撤掉探测碰撞体")]
    public static void Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Run;
            return;
        }

        var madeReadable = new List<string>();
        var probeDisabled = new List<string>();
        var alreadyReadable = new HashSet<string>();

        foreach (string guid in AssetDatabase.FindAssets("t:TerrainDecorationDefinition"))
        {
            var def = AssetDatabase.LoadAssetAtPath<TerrainDecorationDefinition>(
                AssetDatabase.GUIDToAssetPath(guid));
            if (def == null || def.occlusionMode == TerrainDecorationOcclusionMode.None)
                continue;

            // 撤掉误加的探测碰撞体。Builder 在开关为 false 时会把已生成的删掉。
            if (def.generateOccluderProbeCollider)
            {
                Undo.RecordObject(def, "Disable Occluder Probe");
                def.generateOccluderProbeCollider = false;
                EditorUtility.SetDirty(def);
                probeDisabled.Add(Describe(def));
            }

            if (def.variants == null)
                continue;

            foreach (var variant in def.variants)
            {
                if (variant == null || variant.prefab == null)
                    continue;

                foreach (MeshFilter mf in variant.prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null)
                        continue;

                    string modelPath = AssetDatabase.GetAssetPath(mf.sharedMesh);
                    if (string.IsNullOrEmpty(modelPath) || !alreadyReadable.Add(modelPath))
                        continue;

                    if (AssetImporter.GetAtPath(modelPath) is not ModelImporter importer)
                        continue;

                    if (importer.isReadable)
                        continue;

                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    madeReadable.Add(modelPath);
                }
            }
        }

        AssetDatabase.SaveAssets();

        if (madeReadable.Count == 0 && probeDisabled.Count == 0)
        {
            Debug.Log($"{LogPrefix} 没有需要处理的：遮挡模型都已可读，也没有残留的探测碰撞体。");
            return;
        }

        // 定义和导入设置改了都不会回溯已放置的实例，必须重建。
        SkyPrisonOcclusionStructureAuditAndRebuild_V1.RebuildAllSceneThroughBuilder();
        bool saved = EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            $"{LogPrefix} 完成。\n" +
            $"  改为可读的模型（{madeReadable.Count}）：{string.Join("、", madeReadable)}\n" +
            $"  关掉探测碰撞体的定义（{probeDisabled.Count}）：{string.Join("、", probeDisabled)}\n" +
            $"  已重建场景实例，场景保存：{(saved ? "成功" : "失败或被取消")}。");
    }

    private static string Describe(TerrainDecorationDefinition def)
        => string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName;
}
