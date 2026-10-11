using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 把余穹基地的真实布局烘成沙盘用的六边形微缩模型：临时给 BackgroundRoot 下的网格加碰撞体，自上往下打射线取每个六边形格的最高点，
/// 缩放后做成六边形棱柱，存成 Mesh 资源（YuqiongMini.asset）。烘完不保存余穹场景，不动它。
/// </summary>
public static class SkyPrisonRaidDioramaYuqiongBaker
{
    const string StillVault = "Assets/_Project/Maps/Hub/StillVault/Still Vault/Still Vault.unity";
    public const string MeshPath = "Assets/_Project/Proto/RaidDiorama/Meshes/YuqiongMini.asset";
    const float Scale = 0.035f;       // 1 米 → 0.035 沙盘单位
    const float HexR = 0.13f;         // 沙盘里六边形外接圆半径（≈3.7 米），比之前粗，细墙会糊掉只剩大体块

    [MenuItem("天空囚笼/RAID/烘焙余穹微缩模型", false, 301)]
    public static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[余穹微缩] 运行中，没有执行。"); return; }
        var sc = EditorSceneManager.OpenScene(StillVault, OpenSceneMode.Additive);
        try
        {
            GameObject bg = null;
            foreach (var root in sc.GetRootGameObjects()) if (root.name == "WorldRoot") { var t = root.transform.Find("BackgroundRoot"); if (t != null) bg = t.gameObject; }
            if (bg == null) { Debug.LogError("[余穹微缩] 找不到 WorldRoot/BackgroundRoot"); return; }

            var added = new List<MeshCollider>();
            foreach (var mf in bg.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>(); if (r == null || !r.enabled || mf.sharedMesh == null) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added.Add(mc);
            }
            Physics.SyncTransforms();

            var rs = bg.GetComponentsInChildren<MeshRenderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float x0 = b.min.x, x1 = b.max.x, z0 = b.min.z, z1 = b.max.z, cx = b.center.x, cz = b.center.z;
            float R = HexR / Scale, sx = R * 1.7320508f, sz = R * 1.5f;
            var hits = new List<Vector3>(); float minY = float.MaxValue;
            for (int row = (int)((z0 - cz) / sz) - 1; row <= (int)((z1 - cz) / sz) + 1; row++)
                for (int col = (int)((x0 - cx) / sx) - 1; col <= (int)((x1 - cx) / sx) + 1; col++)
                {
                    float px = cx + col * sx + ((row & 1) != 0 ? sx * 0.5f : 0f), pz = cz + row * sz;
                    float top = float.NegativeInfinity;
                    for (int k = -1; k < 6; k++)
                    {
                        float an = k * Mathf.PI / 3f; float ox = k < 0 ? 0 : Mathf.Cos(an) * R * 0.7f, oz = k < 0 ? 0 : Mathf.Sin(an) * R * 0.7f;
                        if (Physics.Raycast(new Vector3(px + ox, b.max.y + 20f, pz + oz), Vector3.down, out var hit, b.size.y + 60f)) top = Mathf.Max(top, hit.point.y);
                    }
                    if (float.IsNegativeInfinity(top)) continue;
                    hits.Add(new Vector3((px - cx) * Scale, top, (pz - cz) * Scale)); minY = Mathf.Min(minY, top);
                }
            var cells = new List<Vector3>(hits.Count);
            foreach (var h in hits) cells.Add(new Vector3(h.x, 0.05f + (h.y - minY) * Scale * 2.2f, h.z));
            var mesh = SkyPrisonRaidDioramaProtoBuilder.HexMesh(cells, HexR * 1.01f);
            Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
            AssetDatabase.DeleteAsset(MeshPath); mesh.name = "YuqiongMini"; AssetDatabase.CreateAsset(mesh, MeshPath); AssetDatabase.SaveAssets();
            Debug.Log($"[余穹微缩] 六边形 {cells.Count} 个，占地 {(x1 - x0) * Scale:F1} × {(z1 - z0) * Scale:F1}，最高 {(b.max.y - minY) * Scale:F2}");
        }
        finally { EditorSceneManager.CloseScene(sc, true); }
    }
}
