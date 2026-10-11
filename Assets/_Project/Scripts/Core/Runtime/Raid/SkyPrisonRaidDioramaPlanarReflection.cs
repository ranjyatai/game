using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 沙盘地面（海洋 / 河道 / 路面）的平面反射：URP 没有屏幕空间反射，所以每帧用一台镜像相机把沙盘倒过来多渲一遍，
/// 再由 Shaders/DioramaPlanarReflection 按主相机的屏幕坐标叠在地面上。倒影里能看到六边形、塔和据点。
/// 参数（强度 / 模糊 / 分辨率）直接在 Inspector 里手调。
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class SkyPrisonRaidDioramaPlanarReflection : MonoBehaviour
{
    [Range(0f, 3f)] public float strength = 0.75f;
    [Range(0f, 8f)] public float blur = 1.6f;
    [Range(0.25f, 1f)] public float resolution = 0.5f;
    public Color tint = Color.white;   // 倒影不加染色，和被映出的东西同色
    [Range(0f, 1f)] public float fresnelMin = 0.5f;
    public float maxCameraDistance = 130f;

    Camera src, rc; RenderTexture rt; Material mat; Renderer overlay; Transform ground;

    void Start()
    {
        var ctl = GetComponent<SkyPrisonRaidDioramaController>(); src = ctl != null ? ctl.cam : null;
        ground = transform.Find("Ground");
        var sh = Resources.Load<Shader>("Shaders/DioramaPlanarReflection");
        if (src == null || ground == null || sh == null) { enabled = false; return; }

        mat = new Material(sh) { name = "DioramaPlanarReflection" };
        var go = new GameObject("PlanarReflectionOverlay", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(ground.parent, false); go.transform.localPosition = ground.localPosition + Vector3.up * 0.012f;
        go.transform.localRotation = ground.localRotation; go.transform.localScale = ground.localScale;
        go.GetComponent<MeshFilter>().sharedMesh = ground.GetComponent<MeshFilter>().sharedMesh;
        overlay = go.GetComponent<MeshRenderer>(); overlay.sharedMaterial = mat;
        overlay.shadowCastingMode = ShadowCastingMode.Off; overlay.receiveShadows = false;

        var cgo = new GameObject("PlanarReflectionCamera"); cgo.transform.SetParent(transform, false);
        rc = cgo.AddComponent<Camera>(); rc.enabled = false;
        var data = cgo.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false; data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
        data.antialiasing = AntialiasingMode.None;
    }

    void EnsureRT()
    {
        int w = Mathf.Max(64, Mathf.RoundToInt((src.targetTexture != null ? src.targetTexture.width : src.pixelWidth) * resolution));
        int h = Mathf.Max(64, Mathf.RoundToInt((src.targetTexture != null ? src.targetTexture.height : src.pixelHeight) * resolution));
        if (rt != null && rt.width == w && rt.height == h) return;
        if (rt != null) rt.Release();
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "DioramaPlanarReflectionRT", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        rt.Create();
    }

    void LateUpdate()
    {
        if (src == null || rc == null) return;
        // 镜头离得太远时倒影看不清，省掉这次额外渲染
        if (!src.isActiveAndEnabled || (src.transform.position - ground.position).magnitude > maxCameraDistance + 80f) { overlay.enabled = false; return; }
        overlay.enabled = true;
        EnsureRT();
        mat.SetTexture("_ReflTex", rt); mat.SetColor("_Tint", tint); mat.SetFloat("_Strength", strength); mat.SetFloat("_Blur", blur); mat.SetFloat("_FresnelMin", fresnelMin);

        Vector3 n = Vector3.up, p = ground.position;
        float d = -Vector3.Dot(n, p);
        Matrix4x4 R = Reflection(new Vector4(n.x, n.y, n.z, d));

        rc.CopyFrom(src); rc.targetTexture = null; rc.enabled = false; rc.clearFlags = src.clearFlags; rc.backgroundColor = src.backgroundColor;
        rc.cullingMask = src.cullingMask & ~(1 << SkyPrisonRaidDioramaMarker.MarkerLayer);
        Vector3 sp = src.transform.position;
        rc.transform.SetPositionAndRotation(R.MultiplyPoint(sp), Quaternion.LookRotation(R.MultiplyVector(src.transform.forward), R.MultiplyVector(src.transform.up)));
        rc.worldToCameraMatrix = src.worldToCameraMatrix * R;

        // 斜裁剪：只渲染地面以上的东西（地面和地板本身不进倒影）
        Matrix4x4 w2c = rc.worldToCameraMatrix;
        Vector3 cpos = w2c.MultiplyPoint(p + n * 0.01f), cn = w2c.MultiplyVector(n).normalized;
        rc.projectionMatrix = src.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cpos, cn)));

        overlay.enabled = false;                       // 渲倒影时叠层自己不能出现（它在读这张 RT）
        GL.invertCulling = true;
        var req = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
        if (RenderPipeline.SupportsRenderRequest(rc, req)) RenderPipeline.SubmitRenderRequest(rc, req);
        GL.invertCulling = false;
        overlay.enabled = true;
    }

    static Matrix4x4 Reflection(Vector4 pl)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * pl.x * pl.x; m.m01 = -2f * pl.x * pl.y; m.m02 = -2f * pl.x * pl.z; m.m03 = -2f * pl.w * pl.x;
        m.m10 = -2f * pl.y * pl.x; m.m11 = 1f - 2f * pl.y * pl.y; m.m12 = -2f * pl.y * pl.z; m.m13 = -2f * pl.w * pl.y;
        m.m20 = -2f * pl.z * pl.x; m.m21 = -2f * pl.z * pl.y; m.m22 = 1f - 2f * pl.z * pl.z; m.m23 = -2f * pl.w * pl.z;
        return m;
    }

    void OnDestroy() { if (rt != null) rt.Release(); if (mat != null) Destroy(mat); }
}
