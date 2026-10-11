using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 排查"AI 能穿墙"：上一轮只查、只修了场景里一个"废路电车"实例的 CollisionRoot，
/// 结果发现问题不止这一处——场景里所有装饰物的 CollisionRoot 下都可能存在同样
/// 被单独禁用的 __PhysicsMeshCollider_NN_... 代理物体（根因见
/// SkyPrisonTerrainDecorationInstanceBuilder.BuildMeshCollisionProxies 里的
/// LOD 过滤逻辑——只给 LOD0 生成碰撞代理，但这批代理生成时如果直接复制了源
/// Renderer 当时的 GameObject 启用状态，源物体因为別的原因（比如美术在
/// Prefab/FBX 里就把某个子网格默认关掉）恰好是禁用的，代理也就跟着生成成禁用的。
///
/// 这次不再一个个实例排查，直接扫全场景所有 CollisionRoot，一次性把里面所有
/// 单独被禁用、但父链都是激活状态的碰撞体代理全部激活。查完删。
/// </summary>
[InitializeOnLoad]
public static class SkyPrisonSceneWideColliderReactivator
{
    static SkyPrisonSceneWideColliderReactivator()
    {
        EditorApplication.delayCall += Run;
    }

    private static void Run()
    {
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        var collisionRoots = new List<Transform>();
        foreach (Transform t in all)
        {
            if (t.name == "CollisionRoot")
                collisionRoots.Add(t);
        }

        Debug.Log($"[全场景碰撞体激活] 场景里共找到 {collisionRoots.Count} 个名为 CollisionRoot 的物体。");

        int totalReactivated = 0;
        int totalRootsAffected = 0;
        var affectedRootPaths = new List<string>();

        foreach (Transform collisionRoot in collisionRoots)
        {
            // CollisionRoot 自己没激活的话，问题在更上层（父物体禁用），这类不是这次
            // 要处理的目标——那种情况本来就是"整个装饰物都没生成/被关掉"，跟"代理
            // 单独被禁用"是两回事，硬激活可能把不该出现的东西显示出来。只处理
            // "CollisionRoot 自己是激活的，但底下个别代理被单独关掉"这一种情况。
            if (!collisionRoot.gameObject.activeInHierarchy)
                continue;

            Collider[] colliders = collisionRoot.GetComponentsInChildren<Collider>(true);
            int reactivatedHere = 0;
            foreach (Collider c in colliders)
            {
                if (c.gameObject.activeSelf)
                    continue;

                Undo.RecordObject(c.gameObject, "Activate physics mesh collider proxy");
                c.gameObject.SetActive(true);
                EditorUtility.SetDirty(c.gameObject);
                reactivatedHere++;
            }

            if (reactivatedHere > 0)
            {
                totalReactivated += reactivatedHere;
                totalRootsAffected++;
                affectedRootPaths.Add($"{GetPath(collisionRoot)} (+{reactivatedHere})");
            }
        }

        if (totalReactivated > 0)
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[全场景碰撞体激活] 一共激活了 {totalReactivated} 个碰撞体代理，涉及 {totalRootsAffected} 个装饰物实例：");
            foreach (string p in affectedRootPaths)
                sb.AppendLine($"  {p}");
            Debug.Log(sb.ToString());
        }
        else
        {
            Debug.Log("[全场景碰撞体激活] 没有发现被单独禁用的碰撞体代理——如果 AI 还是能穿墙，问题不在这一类原因上。");
        }
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
