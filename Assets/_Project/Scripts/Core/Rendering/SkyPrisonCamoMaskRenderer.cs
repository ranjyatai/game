using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 光学迷彩的屏幕空间遮罩：每帧在"渲染迷彩单位的那台相机"开始渲染时(beginCameraRendering——
/// 只有这时相机 Transform 是 Cinemachine 定位完的最终值，LateUpdate 里读到的是上一帧)，
/// 把所有迷彩单位的 Spine 网格画成白色剪影 → 低分辨率 RT → 多次模糊 → 设成全局纹理 _SkyPrisonCamoMask。
/// 歪曲层(Hidden/SP/CamoWarp)读它：整体一块、边缘柔软淡出，没有部件接缝也没有轮廓线。
/// </summary>
public static class SkyPrisonCamoMaskRenderer
{
    public struct Entry
    {
        public Renderer renderer;      // Spine 本体的 MeshRenderer
        public float weight;           // 移动程度 × 未现形比例(0 = 不画)
        public float vertexAlphaScale; // 骨架 alpha 被压低了，网格顶点 alpha 要除回去
        public Matrix4x4 warpMatrix;   // 歪曲面片(单位四边形)的世界矩阵：怪人周围、面向相机、比身体大
        public float warpStrength;
    }

    /// <summary>当前需要画歪曲的单位(权重 > 0)。给 SkyPrisonCamoWarpFeature 用。</summary>
    public static void CollectActive(List<Entry> into)
    {
        into.Clear();
        foreach (var kv in _entries)
            if (kv.Value.renderer != null && kv.Value.weight > 0.001f) into.Add(kv.Value);
    }

    static readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
    static CommandBuffer _cmd;
    static Material _mat;
    static RenderTexture _rtA, _rtB;
    static bool _hooked;
    static readonly int MaskId = Shader.PropertyToID("_SkyPrisonCamoMask");
    static readonly int AtlasId = Shader.PropertyToID("_CamoAtlas");
    static readonly int VaId = Shader.PropertyToID("_CamoVertexAlphaScale");
    static readonly int WeightId = Shader.PropertyToID("_CamoWeight");
    static readonly int BlurDirId = Shader.PropertyToID("_BlurDir");

    public const int Downsample = 4;
    public const int BlurIterations = 3;
    public const float BlurStep = 1.6f;     // 每次模糊的步长(低分辨率像素)

    public static void Set(int id, Entry e) { _entries[id] = e; EnsureHook(); }
    public static void Remove(int id) { _entries.Remove(id); }

    static void EnsureHook()
    {
        if (_hooked) return;
        _hooked = true;
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (_hooked) RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        _hooked = false;
        _entries.Clear();
    }

    static void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.SceneView) return;
        // 只在渲染迷彩单位所在图层的相机上画(主相机)
        bool any = false;
        foreach (var kv in _entries)
        {
            var r = kv.Value.renderer;
            if (r != null && (cam.cullingMask & (1 << r.gameObject.layer)) != 0) { any = true; break; }
        }
        if (!any) { if (_rtA != null) Shader.SetGlobalTexture(MaskId, Texture2D.blackTexture); return; }

        if (_mat == null)
        {
            var sh = Shader.Find("Hidden/SP/CamoMask");
            if (sh == null) return;
            _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }
        int w = Mathf.Max(16, cam.pixelWidth / Downsample), h = Mathf.Max(16, cam.pixelHeight / Downsample);
        EnsureRT(ref _rtA, w, h, "CamoMaskA");
        EnsureRT(ref _rtB, w, h, "CamoMaskB");
        if (_cmd == null) _cmd = new CommandBuffer { name = "SkyPrison Camo Mask" };
        _cmd.Clear();

        _cmd.SetRenderTarget(_rtA);
        _cmd.ClearRenderTarget(false, true, Color.clear);
        _cmd.SetViewProjectionMatrices(cam.worldToCameraMatrix, cam.projectionMatrix);
        foreach (var kv in _entries)
        {
            var e = kv.Value;
            if (e.renderer == null || e.weight <= 0.001f || !e.renderer.enabled) continue;
            if ((cam.cullingMask & (1 << e.renderer.gameObject.layer)) == 0) continue;
            var mats = e.renderer.sharedMaterials;
            _cmd.SetGlobalFloat(VaId, e.vertexAlphaScale);
            _cmd.SetGlobalFloat(WeightId, e.weight);
            for (int i = 0; i < mats.Length; i++)
            {
                var tex = mats[i] != null ? mats[i].mainTexture : null;
                _cmd.SetGlobalTexture(AtlasId, tex != null ? tex : Texture2D.whiteTexture);
                _cmd.DrawRenderer(e.renderer, _mat, i, 0);
            }
        }

        for (int it = 0; it < BlurIterations; it++)
        {
            _cmd.SetGlobalVector(BlurDirId, new Vector4(BlurStep / w, 0f, 0f, 0f));
            _cmd.Blit(_rtA, _rtB, _mat, 1);
            _cmd.SetGlobalVector(BlurDirId, new Vector4(0f, BlurStep / h, 0f, 0f));
            _cmd.Blit(_rtB, _rtA, _mat, 1);
        }
        _cmd.SetGlobalTexture(MaskId, _rtA);
        ctx.ExecuteCommandBuffer(_cmd);
        _cmd.Clear();
    }

    static void EnsureRT(ref RenderTexture rt, int w, int h, string name)
    {
        if (rt != null && rt.width == w && rt.height == h) return;
        if (rt != null) rt.Release();
        var fmt = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? RenderTextureFormat.R8 : RenderTextureFormat.ARGB32;
        rt = new RenderTexture(w, h, 0, fmt) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        rt.Create();
    }

    /// <summary>调试：把当前遮罩读回 CPU，返回最亮像素的位置(0~1 视口坐标)和亮度。</summary>
    public static Vector3 DebugPeak()
    {
        if (_rtA == null) return new Vector3(-1, -1, 0);
        var prev = RenderTexture.active;
        RenderTexture.active = _rtA;
        var tex = new Texture2D(_rtA.width, _rtA.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, _rtA.width, _rtA.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        float best = 0f; int bx = -1, by = -1;
        var px = tex.GetPixels();
        for (int y = 0; y < tex.height; y++)
            for (int x = 0; x < tex.width; x++)
            {
                float v = px[y * tex.width + x].r;
                if (v > best) { best = v; bx = x; by = y; }
            }
        Object.Destroy(tex);
        return new Vector3(bx / (float)tex.width, by / (float)tex.height, best);
    }
}
