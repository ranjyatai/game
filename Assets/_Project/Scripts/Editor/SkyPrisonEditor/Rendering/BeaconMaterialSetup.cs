#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

static class BeaconMaterialSetup
{
    [MenuItem("SkyPrison/Rendering/创建 Beacon 粒子材质并赋值")]
    static void Create()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            EditorUtility.DisplayDialog("错误", "找不到 Universal Render Pipeline/Particles/Unlit，请确认已安装 URP。", "OK");
            return;
        }

        var mat = new Material(shader) { name = "BeaconParticle" };

        // Transparent + Additive
        mat.SetFloat("_Surface", 1);                                // Transparent
        mat.SetFloat("_Blend",   2);                                // Additive
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_ZWrite",   0);
        mat.SetFloat("_BlendModePreserveSpecular", 0);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetColor("_BaseColor", Color.white);

        // 保存到 Art/Materials/
        const string dir  = "Assets/_Project/Art/Materials";
        const string path = dir + "/BeaconParticle.mat";
        System.IO.Directory.CreateDirectory(dir);

        // 已存在则覆盖属性，不重复创建
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            EditorUtility.CopySerialized(mat, existing);
            EditorUtility.SetDirty(existing);
            mat = existing;
        }
        else
        {
            AssetDatabase.CreateAsset(mat, path);
        }

        // 自动赋值给所有 LootDropModelLibrary
        string[] guids = AssetDatabase.FindAssets("t:LootDropModelLibrary");
        int assigned = 0;
        foreach (string guid in guids)
        {
            string libPath = AssetDatabase.GUIDToAssetPath(guid);
            var lib = AssetDatabase.LoadAssetAtPath<LootDropModelLibrary>(libPath);
            if (lib == null) continue;
            lib.beaconMaterial = mat;
            EditorUtility.SetDirty(lib);
            assigned++;
        }

        AssetDatabase.SaveAssets();

        EditorGUIUtility.PingObject(mat);
        Selection.activeObject = mat;

        Debug.Log($"[BeaconMaterial] 已创建 {path}，赋值到 {assigned} 个 LootDropModelLibrary。");
        EditorUtility.DisplayDialog("完成", $"材质已创建并自动赋值到 {assigned} 个 Library。", "OK");
    }
}
#endif
