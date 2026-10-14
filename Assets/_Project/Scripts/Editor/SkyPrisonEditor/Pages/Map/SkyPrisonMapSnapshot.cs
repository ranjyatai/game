using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 地图编辑器的俯视快照：用正交相机从上往下拍 MapBounds 范围，存成 PNG（Assets/_Project/Art/MapSnapshots/&lt;mapKey&gt;.png）。
/// 地图场景没打开时，以 Additive 方式临时载入再关掉，不会碰当前场景（含未保存的改动）。
/// </summary>
public static class SkyPrisonMapSnapshot
{
    const string Dir = "Assets/_Project/Art/MapSnapshots";
    const int MaxSide = 1024;

    public static string PathFor(MapDefinition map) => $"{Dir}/{Sanitize(map.name)}.png";   // 用地图资源名：mapKey 目前很多地图都是默认的 newmap，会互相覆盖

    static string Sanitize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "map";
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    public static Texture2D Load(MapDefinition map)
    {
        if (map == null) return null;
        return AssetDatabase.LoadAssetAtPath<Texture2D>(PathFor(map));
    }

    public static bool Capture(MapDefinition map, Vector3 center, Vector3 size, out string error)
    {
        error = null;
        if (map == null || string.IsNullOrWhiteSpace(map.scenePath)) { error = "地图没有关联 Scene"; return false; }
        if (Application.isPlaying) { error = "Play 模式下不能拍快照"; return false; }

        Scene scene = SceneManager.GetSceneByPath(map.scenePath);
        bool openedHere = false;
        if (!scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(map.scenePath, OpenSceneMode.Additive);
            openedHere = true;
        }

        try
        {
            float sx = Mathf.Max(1f, size.x), sz = Mathf.Max(1f, size.z), sy = Mathf.Max(5f, size.y);
            float k = MaxSide / Mathf.Max(sx, sz);
            int w = Mathf.Max(16, Mathf.RoundToInt(sx * k)), h = Mathf.Max(16, Mathf.RoundToInt(sz * k));

            var go = new GameObject("__MapSnapshotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = sz * 0.5f;
            cam.aspect = sx / sz;
            cam.transform.position = new Vector3(center.x, center.y + sy * 0.5f + 1f, center.z);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = sy + 2f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.06f, 0.07f, 1f);
            cam.allowHDR = false; cam.allowMSAA = false;

            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(go);

            Directory.CreateDirectory(Dir);
            File.WriteAllBytes(PathFor(map), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PathFor(map), ImportAssetOptions.ForceUpdate);
            return true;
        }
        finally
        {
            if (openedHere && scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
        }
    }
}
