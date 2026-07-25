using UnityEngine;
using UnityEditor;
using System.IO;

public static class CreateHexPrismMesh
{
    private const string SavePath = "Assets/_Project/Art/Models/Drop/MD_Special.asset";

    [MenuItem("SkyPrison/Tools/生成正二十面体 Mesh (MD_Special)")]
    public static void Create()
    {
        Mesh mesh = BuildIcosahedron(0.1f);
        mesh.name = "MD_Special";

        string dir = Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", SavePath));
        Directory.CreateDirectory(dir);
        AssetDatabase.CreateAsset(mesh, SavePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[MD_Special] 正二十面体已保存到 {SavePath}");
        EditorGUIUtility.PingObject(mesh);
    }

    // 正二十面体：12顶点，20个等边三角形面
    private static Mesh BuildIcosahedron(float radius)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f; // 黄金比例

        // 12个顶点（归一化后乘以radius）
        Vector3[] src =
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0),
            new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t),
            new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1),
            new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
        };

        // 20个面（顶点索引）
        int[] faces =
        {
            0,11, 5,   0, 5, 1,   0, 1, 7,   0, 7,10,   0,10,11,
            1, 5, 9,   5,11, 4,  11,10, 2,  10, 7, 6,   7, 1, 8,
            3, 9, 4,   3, 4, 2,   3, 2, 6,   3, 6, 8,   3, 8, 9,
            4, 9, 5,   2, 4,11,   6, 2,10,   8, 6, 7,   9, 8, 1,
        };

        // 每个面独立顶点（保证面法线正确，共20*3=60个顶点）
        var verts   = new Vector3[60];
        var normals = new Vector3[60];
        var uvs     = new Vector2[60];
        var tris    = new int[60];

        for (int i = 0; i < 20; i++)
        {
            int a = faces[i * 3], b = faces[i * 3 + 1], c = faces[i * 3 + 2];
            Vector3 va = src[a].normalized * radius;
            Vector3 vb = src[b].normalized * radius;
            Vector3 vc = src[c].normalized * radius;

            Vector3 n = Vector3.Cross(vb - va, vc - va).normalized;

            int idx = i * 3;
            verts[idx]   = va; verts[idx+1] = vb; verts[idx+2] = vc;
            normals[idx] = n;  normals[idx+1] = n; normals[idx+2] = n;
            uvs[idx]     = new Vector2(0.5f + va.x, 0.5f + va.z);
            uvs[idx+1]   = new Vector2(0.5f + vb.x, 0.5f + vb.z);
            uvs[idx+2]   = new Vector2(0.5f + vc.x, 0.5f + vc.z);
            tris[idx] = idx; tris[idx+1] = idx+1; tris[idx+2] = idx+2;
        }

        var mesh = new Mesh();
        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}
