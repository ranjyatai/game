using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SkyPrisonGroundOverlayRouteProbe_Safe
{
    static SkyPrisonGroundOverlayRouteProbe_Safe()
    {
        Debug.Log("[RoadLine SafeProbe] Loaded. Menus: Tools/Sky Prison/Debug/RoadLine 路线诊断（Safe）");
    }

    [MenuItem("Tools/Sky Prison/Debug/RoadLine 路线诊断（Safe）", false, 10)]
    public static void DiagnoseSafe()
    {
        Terrain terrain = FindActiveGroundTerrain();
        Debug.Log($"[RoadLine SafeProbe] GroundTerrain = {(terrain != null ? GetPath(terrain.transform) : "NULL")}");

        GameObject overlayGo = GameObject.Find("WorldRoot/GroundRoot/GroundOverlay_RoadLine")
            ?? GameObject.Find("GroundOverlay_RoadLine");

        if (overlayGo == null)
        {
            Debug.LogWarning("[RoadLine SafeProbe] 没找到 GroundOverlay_RoadLine。说明 Overlay 显示对象没有建立，或者路径不对。");
        }
        else
        {
            Debug.Log($"[RoadLine SafeProbe] Overlay GameObject = {GetPath(overlayGo.transform)}, active={overlayGo.activeInHierarchy}", overlayGo);
            MeshFilter mf = overlayGo.GetComponent<MeshFilter>();
            MeshRenderer mr = overlayGo.GetComponent<MeshRenderer>();
            Component layer = GetComponentByTypeName(overlayGo, "SkyPrisonGroundOverlayLayer");
            Debug.Log($"[RoadLine SafeProbe] MeshFilter={(mf != null ? "YES" : "NO")}, MeshRenderer={(mr != null ? "YES" : "NO")}, OverlayLayer={(layer != null ? "YES" : "NO")}", overlayGo);

            if (mf != null)
                Debug.Log($"[RoadLine SafeProbe] Mesh={(mf.sharedMesh != null ? mf.sharedMesh.name + " v=" + mf.sharedMesh.vertexCount : "NULL")}", mf);

            if (mr != null)
            {
                Debug.Log($"[RoadLine SafeProbe] Renderer.enabled={mr.enabled}, material={(mr.sharedMaterial != null ? mr.sharedMaterial.name : "NULL")}", mr);
                if (mr.sharedMaterial != null)
                    LogMaterialTextures(mr.sharedMaterial);
            }

            if (layer != null)
                LogOverlayLayerFields(layer);
        }

        DiagnoseTerrainRoadLineLayers(terrain);
    }

    [MenuItem("Tools/Sky Prison/Debug/RoadLine 强制重建显示层（Safe）", false, 11)]
    public static void ForceRebuildSafe()
    {
        Terrain terrain = FindActiveGroundTerrain();
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogError("[RoadLine SafeProbe] 找不到 GroundTerrain，无法重建 Overlay 显示层。");
            return;
        }

        Transform root = FindOrCreatePath("WorldRoot/GroundRoot");
        Transform existing = root.Find("GroundOverlay_RoadLine");
        GameObject go = existing != null ? existing.gameObject : new GameObject("GroundOverlay_RoadLine");
        Undo.RegisterFullObjectHierarchyUndo(go, "Force Rebuild RoadLine Overlay Safe");
        go.transform.SetParent(root, false);
        go.transform.position = terrain.transform.position;
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.SetActive(true);

        MeshFilter mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.AddComponent<MeshRenderer>();

        Type layerType = FindType("SkyPrisonGroundOverlayLayer");
        Component layer = null;
        if (layerType != null)
        {
            layer = GetComponentByTypeName(go, "SkyPrisonGroundOverlayLayer") ?? go.AddComponent(layerType);
            SetFieldOrProperty(layer, "targetTerrain", terrain);
            SetFieldOrProperty(layer, "overlayResolution", 8192);
            SetFieldOrProperty(layer, "overlayRenderer", mr);
            SetFieldOrProperty(layer, "materialTextureProperty", "_BaseColorMap");
            InvokeIfExists(layer, "EnsureTexture");
        }
        else
        {
            Debug.LogWarning("[RoadLine SafeProbe] 没找到 SkyPrisonGroundOverlayLayer 类型，只会重建 MeshRenderer，无法绑定数据层。");
        }

        Texture2D overlayTexture = layer != null ? GetFieldOrProperty(layer, "overlayTexture") as Texture2D : null;
        if (overlayTexture == null)
        {
            overlayTexture = CreateTransientOverlayTexture(8192);
            Debug.LogWarning("[RoadLine SafeProbe] OverlayLayer 没有返回 overlayTexture，临时创建一张贴图用于材质绑定。", overlayTexture);
        }

        mf.sharedMesh = BuildTerrainOverlayMesh(terrain, 192, 0.05f);
        mr.sharedMaterial = GetOrCreateOverlayMaterial(overlayTexture);
        mr.enabled = true;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.allowOcclusionWhenDynamic = false;
        mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        BindTexture(mr.sharedMaterial, overlayTexture);

        if (layer != null)
            InvokeIfExists(layer, "BindTextureToRenderer");

        EditorUtility.SetDirty(go);
        EditorUtility.SetDirty(mf);
        EditorUtility.SetDirty(mr);
        if (layer != null) EditorUtility.SetDirty(layer);
        Selection.activeGameObject = go;
        Debug.Log($"[RoadLine SafeProbe] 已强制重建显示层：{GetPath(go.transform)}，OverlayTexture={(overlayTexture != null ? overlayTexture.width + "x" + overlayTexture.height : "NULL")}", go);
    }

    [MenuItem("Tools/Sky Prison/Debug/RoadLine 直接画测试线（Safe）", false, 12)]
    public static void PaintDiagnosticLineSafe()
    {
        GameObject overlayGo = GameObject.Find("WorldRoot/GroundRoot/GroundOverlay_RoadLine")
            ?? GameObject.Find("GroundOverlay_RoadLine");
        if (overlayGo == null)
        {
            Debug.LogError("[RoadLine SafeProbe] 没有 GroundOverlay_RoadLine。先执行 RoadLine 强制重建显示层（Safe）。");
            return;
        }

        Component layer = GetComponentByTypeName(overlayGo, "SkyPrisonGroundOverlayLayer");
        if (layer == null)
        {
            Debug.LogError("[RoadLine SafeProbe] GroundOverlay_RoadLine 上没有 SkyPrisonGroundOverlayLayer。", overlayGo);
            return;
        }

        Texture2D tex = GetFieldOrProperty(layer, "overlayTexture") as Texture2D;
        if (tex == null)
        {
            InvokeIfExists(layer, "EnsureTexture");
            tex = GetFieldOrProperty(layer, "overlayTexture") as Texture2D;
        }
        if (tex == null)
        {
            Debug.LogError("[RoadLine SafeProbe] OverlayTexture 为空，无法直接画测试线。", overlayGo);
            return;
        }

        // Directly paint in texture UV-space: a crisp diagonal band.
        Undo.RegisterCompleteObjectUndo(tex, "Paint RoadLine Diagnostic Safe");
        int w = tex.width;
        int h = tex.height;
        Color32 white = new Color32(255, 255, 255, 255);
        int thickness = Mathf.Max(10, w / 120);
        for (int y = h / 4; y < h * 3 / 4; y++)
        {
            float t = Mathf.InverseLerp(h / 4f, h * 3f / 4f, y);
            int xCenter = Mathf.RoundToInt(Mathf.Lerp(w / 4f, w * 3f / 4f, t));
            for (int dx = -thickness; dx <= thickness; dx++)
            {
                int x = xCenter + dx;
                if (x >= 0 && x < w)
                    tex.SetPixel(x, y, white);
            }
        }
        tex.Apply(false, false);
        EditorUtility.SetDirty(tex);
        MeshRenderer mr = overlayGo.GetComponent<MeshRenderer>();
        if (mr != null && mr.sharedMaterial != null) BindTexture(mr.sharedMaterial, tex);
        Debug.Log($"[RoadLine SafeProbe] 已直接写入测试斜线到 OverlayTexture：{tex.name} {w}x{h}", tex);
    }

    private static Terrain FindActiveGroundTerrain()
    {
        GameObject go = GameObject.Find("WorldRoot/GroundRoot/GroundTerrain");
        Terrain t = go != null ? go.GetComponent<Terrain>() : null;
        if (t != null) return t;
        Terrain[] terrains = UnityEngine.Object.FindObjectsOfType<Terrain>(true);
        if (terrains.Length == 1) return terrains[0];
        return Terrain.activeTerrain;
    }

    private static void DiagnoseTerrainRoadLineLayers(Terrain terrain)
    {
        if (terrain == null || terrain.terrainData == null) return;
        TerrainLayer[] layers = terrain.terrainData.terrainLayers;
        bool found = false;
        for (int i = 0; i < layers.Length; i++)
        {
            TerrainLayer tl = layers[i];
            if (tl == null) continue;
            string n = tl.name;
            if (n.IndexOf("RoadLine", StringComparison.OrdinalIgnoreCase) >= 0 || n.Contains("马路线") || n.Contains("画线") || n.Contains("样条"))
            {
                found = true;
                Debug.LogWarning($"[RoadLine SafeProbe] Terrain alphamap 里仍存在疑似 RoadLine TerrainLayer：index={i}, name={n}, path={AssetDatabase.GetAssetPath(tl)}", tl);
            }
        }
        if (!found)
            Debug.Log("[RoadLine SafeProbe] TerrainLayer 检查：没有发现名字疑似 RoadLine 的 TerrainLayer。", terrain);
    }

    private static void LogOverlayLayerFields(Component layer)
    {
        object res = GetFieldOrProperty(layer, "overlayResolution");
        object tex = GetFieldOrProperty(layer, "overlayTexture");
        object renderer = GetFieldOrProperty(layer, "overlayRenderer");
        object autoApply = GetFieldOrProperty(layer, "autoApplyTexture");
        Debug.Log($"[RoadLine SafeProbe] Layer fields: overlayResolution={res}, overlayTexture={DescribeObject(tex)}, overlayRenderer={DescribeObject(renderer)}, autoApply={autoApply}", layer);
    }

    private static void LogMaterialTextures(Material mat)
    {
        string[] props = { "_BaseColorMap", "_UnlitColorMap", "_MainTex", "_BaseMap" };
        foreach (string p in props)
        {
            if (mat.HasProperty(p))
            {
                Texture tex = mat.GetTexture(p);
                Debug.Log($"[RoadLine SafeProbe] Material texture {p} = {DescribeObject(tex)}", mat);
            }
        }
    }

    private static Type FindType(string typeName)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = null;
            try { t = asm.GetTypes().FirstOrDefault(x => x.Name == typeName); }
            catch { }
            if (t != null) return t;
        }
        return null;
    }

    private static Component GetComponentByTypeName(GameObject go, string typeName)
    {
        return go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == typeName);
    }

    private static object GetFieldOrProperty(object target, string name)
    {
        if (target == null) return null;
        Type t = target.GetType();
        FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null) return f.GetValue(target);
        PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanRead) return p.GetValue(target, null);
        return null;
    }

    private static void SetFieldOrProperty(object target, string name, object value)
    {
        if (target == null) return;
        Type t = target.GetType();
        FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
        {
            f.SetValue(target, value);
            return;
        }
        PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanWrite) p.SetValue(target, value, null);
    }

    private static void InvokeIfExists(object target, string methodName)
    {
        if (target == null) return;
        MethodInfo m = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (m != null) m.Invoke(target, null);
    }

    private static string DescribeObject(object obj)
    {
        if (obj == null) return "NULL";
        if (obj is Texture2D t) return $"{t.name} {t.width}x{t.height}";
        if (obj is UnityEngine.Object uo) return uo.name;
        return obj.ToString();
    }

    private static Texture2D CreateTransientOverlayTexture(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
        {
            name = "T_GroundOverlay_RoadLine_Transient_8192",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        Color32[] clear = new Color32[size * size];
        tex.SetPixels32(clear);
        tex.Apply(false, false);
        return tex;
    }

    private static Mesh BuildTerrainOverlayMesh(Terrain terrain, int grid, float heightOffset)
    {
        TerrainData data = terrain.terrainData;
        int vCount = (grid + 1) * (grid + 1);
        Vector3[] vertices = new Vector3[vCount];
        Vector2[] uvs = new Vector2[vCount];
        int[] triangles = new int[grid * grid * 6];
        int vi = 0;
        for (int z = 0; z <= grid; z++)
        {
            float v = z / (float)grid;
            for (int x = 0; x <= grid; x++)
            {
                float u = x / (float)grid;
                float h = data.GetInterpolatedHeight(u, v) + heightOffset;
                vertices[vi] = new Vector3(u * data.size.x, h, v * data.size.z);
                uvs[vi] = new Vector2(u, v);
                vi++;
            }
        }
        int ti = 0;
        int row = grid + 1;
        for (int z = 0; z < grid; z++)
        {
            for (int x = 0; x < grid; x++)
            {
                int a = z * row + x;
                int b = a + 1;
                int c = a + row;
                int d = c + 1;
                triangles[ti++] = a; triangles[ti++] = c; triangles[ti++] = b;
                triangles[ti++] = b; triangles[ti++] = c; triangles[ti++] = d;
            }
        }
        Mesh mesh = new Mesh { name = "MESH_GroundOverlay_RoadLine_SafeRebuild" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material GetOrCreateOverlayMaterial(Texture2D texture)
    {
        string folder = "Assets/_Project/Art/Materials/Ground/Generated";
        EnsureFolder(folder);
        string path = folder + "/M_GroundOverlay_RoadLine.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Shader shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            mat = new Material(shader) { name = "M_GroundOverlay_RoadLine" };
            AssetDatabase.CreateAsset(mat, path);
        }
        ConfigureMaterial(mat);
        BindTexture(mat, texture);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void ConfigureMaterial(Material mat)
    {
        if (mat == null) return;
        if (mat.HasProperty("_SurfaceType")) mat.SetFloat("_SurfaceType", 1f);
        if (mat.HasProperty("_BlendMode")) mat.SetFloat("_BlendMode", 0f);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_AlphaCutoffEnable")) mat.SetFloat("_AlphaCutoffEnable", 0f);
        if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
    }

    private static void BindTexture(Material mat, Texture2D texture)
    {
        if (mat == null || texture == null) return;
        string[] props = { "_BaseColorMap", "_UnlitColorMap", "_MainTex", "_BaseMap" };
        foreach (string p in props)
        {
            if (mat.HasProperty(p))
            {
                mat.SetTexture(p, texture);
                return;
            }
        }
    }

    private static Transform FindOrCreatePath(string path)
    {
        string[] parts = path.Split('/');
        Transform current = null;
        foreach (string part in parts)
        {
            Transform next = current == null ? (GameObject.Find(part)?.transform) : current.Find(part);
            if (next == null)
            {
                GameObject go = new GameObject(part);
                Undo.RegisterCreatedObjectUndo(go, "Create Ground Overlay Path Safe");
                next = go.transform;
                if (current != null) next.SetParent(current, false);
            }
            current = next;
        }
        return current;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string GetPath(Transform t)
    {
        if (t == null) return "NULL";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
