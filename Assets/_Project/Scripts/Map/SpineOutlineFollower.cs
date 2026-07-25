using UnityEngine;
using Spine.Unity;

/// <summary>
/// Sky Prison outline proxy follower.
///
/// V10 compile-fixed direct mesh copy route:
/// - The real Spine object remains the only animation / skeleton owner.
/// - The proxy is reduced to a passive MeshFilter + MeshRenderer display object.
/// - Every frame this component copies the real Spine runtime mesh data into an owned proxy mesh.
/// - It does NOT rely on the proxy's SkeletonAnimation, SkeletonRenderer, TrackTime, skin, slots, or bones.
///
/// Replace the existing SpineOutlineFollower.cs with this file content.
/// The class name intentionally remains SpineOutlineFollower so existing components do not need to be re-added.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(70000)]
public class SpineOutlineFollower : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("真实角色的 SkeletonAnimation。留空时会从当前 OutlineProxy 的父级里自动寻找非 Outline / Proxy 的真实 Spine。")]
    [SerializeField] private SkeletonAnimation sourceSkeletonAnimation;

    [Tooltip("真实角色 Spine 的 MeshFilter。通常不用手填，会从 Source SkeletonAnimation 上读取。")]
    [SerializeField] private MeshFilter sourceMeshFilter;

    [Header("Target")]
    [Tooltip("当前代理体自己的 MeshFilter。留空时默认取当前物体。")]
    [SerializeField] private MeshFilter targetMeshFilter;

    [Tooltip("当前代理体自己的 MeshRenderer。留空时默认取当前物体。材质仍然使用代理体自身材质。")]
    [SerializeField] private MeshRenderer targetMeshRenderer;

    [Header("Mirror Options")]
    [SerializeField] private bool autoResolveReferences = true;

    [Tooltip("开启后，代理体 Transform 每帧复刻真实 Spine MeshFilter 的世界 Transform。复制源 Mesh 顶点时推荐开启。")]
    [SerializeField] private bool mirrorWorldTransform = true;

    [Tooltip("开启后，不引用源 Mesh，而是每帧把源 Mesh 顶点/三角形/UV/颜色复制到代理体自己的 Runtime Mesh。推荐开启。")]
    [SerializeField] private bool copySourceMeshData = true;

    [Tooltip("关闭代理体自己的 Spine Runtime 组件，防止它们继续把代理 Mesh 重置回自己的站姿。")]
    [SerializeField] private bool disableTargetSpineRuntimeComponents = true;

    [Tooltip("确保代理体 MeshRenderer 保持启用。")]
    [SerializeField] private bool forceRendererEnabled = true;

    [Tooltip("自动解析失败时，每隔多少帧重试一次。")]
    [SerializeField] private int autoResolveRetryIntervalFrames = 30;

    [Header("Runtime Debug")]
    [SerializeField] private string resolvedSourcePath = "";
    [SerializeField] private string resolvedSourceMeshPath = "";
    [SerializeField] private string resolvedTargetPath = "";
    [SerializeField] private string sourceMeshName = "";
    [SerializeField] private string targetMeshName = "";
    [SerializeField] private int sourceVertexCount = 0;
    [SerializeField] private int targetVertexCount = 0;
    [SerializeField] private string lastSourceAnimation = "";
    [SerializeField] private string lastFailureReason = "";
    [SerializeField] private int lastSyncFrame = -1;

    private Mesh ownedMirrorMesh;
    private int nextResolveFrame;
    private int lastCopiedFrame = -1;

    // TryCopyMesh 每帧都跑（角色待机呼吸动画本身也在动，描边镜像网格得跟着同步）。
    // 之前直接读 source.vertices/uv/uv2/colors/normals/tangents 这些属性——每一个
    // 在Unity底层都会新分配一份托管数组返回，一帧就是6+个数组分配，乘以每个挂了
    // 描边的单位，是"哪怕站着不动GC也很高"这个症状的主要来源之一。改成
    // GetXxx(List<T>)/SetXxx(List<T>) 这套非分配API，复用同一批List缓冲区。
    private readonly System.Collections.Generic.List<Vector3> _bufVertices = new System.Collections.Generic.List<Vector3>();
    private readonly System.Collections.Generic.List<Vector2> _bufUV = new System.Collections.Generic.List<Vector2>();
    private readonly System.Collections.Generic.List<Vector2> _bufUV2 = new System.Collections.Generic.List<Vector2>();
    private readonly System.Collections.Generic.List<Color> _bufColors = new System.Collections.Generic.List<Color>();
    private readonly System.Collections.Generic.List<Vector3> _bufNormals = new System.Collections.Generic.List<Vector3>();
    private readonly System.Collections.Generic.List<Vector4> _bufTangents = new System.Collections.Generic.List<Vector4>();
    private readonly System.Collections.Generic.List<int> _bufTriangles = new System.Collections.Generic.List<int>();

    private void Reset()
    {
        targetMeshFilter = GetComponent<MeshFilter>();
        targetMeshRenderer = GetComponent<MeshRenderer>();
    }

    private void OnEnable()
    {
        ResolveReferences(true);
        ApplyPassiveProxySettings();
        MirrorNow();
    }

    private void OnDisable()
    {
        DestroyOwnedMirrorMesh();
    }

    private void OnValidate()
    {
        if (targetMeshFilter == null)
            targetMeshFilter = GetComponent<MeshFilter>();

        if (targetMeshRenderer == null)
            targetMeshRenderer = GetComponent<MeshRenderer>();
    }

    private void LateUpdate()
    {
        MirrorNow();
    }

    private void OnWillRenderObject()
    {
        // Safety net: if another Spine component refreshed after LateUpdate, mirror again right before render.
        MirrorNow();
    }

    private void MirrorNow()
    {
        if (autoResolveReferences && NeedsResolve() && Time.frameCount >= nextResolveFrame)
            ResolveReferences(false);

        ApplyPassiveProxySettings();
        MirrorFinalMesh();
    }

    private bool NeedsResolve()
    {
        return sourceSkeletonAnimation == null || sourceMeshFilter == null || targetMeshFilter == null || targetMeshRenderer == null;
    }

    private void ResolveReferences(bool force)
    {
        if (!force && !NeedsResolve())
            return;

        if (targetMeshFilter == null)
            targetMeshFilter = GetComponent<MeshFilter>();

        if (targetMeshRenderer == null)
            targetMeshRenderer = GetComponent<MeshRenderer>();

        if (sourceSkeletonAnimation == null)
            sourceSkeletonAnimation = FindBestSourceSkeletonAnimation();

        if (sourceMeshFilter == null && sourceSkeletonAnimation != null)
            sourceMeshFilter = sourceSkeletonAnimation.GetComponent<MeshFilter>();

        resolvedSourcePath = sourceSkeletonAnimation != null ? GetPath(sourceSkeletonAnimation.transform) : "<none>";
        resolvedSourceMeshPath = sourceMeshFilter != null ? GetPath(sourceMeshFilter.transform) : "<none>";
        resolvedTargetPath = targetMeshFilter != null ? GetPath(targetMeshFilter.transform) : GetPath(transform);

        nextResolveFrame = Time.frameCount + Mathf.Max(1, autoResolveRetryIntervalFrames);
    }

    private void ApplyPassiveProxySettings()
    {
        if (!disableTargetSpineRuntimeComponents)
            return;

        SkeletonRenderer[] skeletonRenderers = GetComponents<SkeletonRenderer>();
        for (int i = 0; i < skeletonRenderers.Length; i++)
        {
            SkeletonRenderer sr = skeletonRenderers[i];
            if (sr == null)
                continue;

            if (sourceSkeletonAnimation != null && sr.transform == sourceSkeletonAnimation.transform)
                continue;

            SkeletonAnimation sa = sr.GetComponent<SkeletonAnimation>();
            if (sa != null)
            {
                sa.AnimationName = null;
                sa.timeScale = 0f;
            }

            sr.enabled = false;
        }
    }

    private void MirrorFinalMesh()
    {
        lastFailureReason = "";

        if (sourceMeshFilter == null)
        {
            lastFailureReason = "Source MeshFilter is missing.";
            return;
        }

        if (targetMeshFilter == null)
        {
            lastFailureReason = "Target MeshFilter is missing.";
            return;
        }

        Mesh sourceMesh = GetSourceMesh(sourceMeshFilter);
        if (sourceMesh == null)
        {
            lastFailureReason = "Source Mesh is null. Check whether the real Spine MeshFilter is the correct source.";
            sourceMeshName = "<null>";
            sourceVertexCount = 0;
            return;
        }

        if (sourceMesh.vertexCount <= 0)
        {
            lastFailureReason = "Source Mesh has 0 vertices.";
            sourceMeshName = sourceMesh.name;
            sourceVertexCount = 0;
            return;
        }

        if (mirrorWorldTransform)
            CopyWorldTransform(sourceMeshFilter.transform, transform);

        if (copySourceMeshData)
        {
            EnsureOwnedMirrorMesh();
            if (!TryCopyMesh(sourceMesh, ownedMirrorMesh))
                return;

            targetMeshFilter.sharedMesh = ownedMirrorMesh;
        }
        else
        {
            targetMeshFilter.sharedMesh = sourceMesh;
        }

        if (targetMeshRenderer != null && forceRendererEnabled)
            targetMeshRenderer.enabled = true;

        Mesh targetMesh = targetMeshFilter.sharedMesh;
        sourceMeshName = sourceMesh.name;
        targetMeshName = targetMesh != null ? targetMesh.name : "<null>";
        sourceVertexCount = sourceMesh.vertexCount;
        targetVertexCount = targetMesh != null ? targetMesh.vertexCount : 0;
        lastSourceAnimation = GetCurrentAnimationName(sourceSkeletonAnimation);
        lastSyncFrame = Time.frameCount;
        lastCopiedFrame = Time.frameCount;
    }

    private static Mesh GetSourceMesh(MeshFilter meshFilter)
    {
        if (meshFilter == null)
            return null;

        Mesh mesh = meshFilter.sharedMesh;
        if (mesh != null)
            return mesh;

        try
        {
            return meshFilter.mesh;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureOwnedMirrorMesh()
    {
        if (ownedMirrorMesh != null)
            return;

        ownedMirrorMesh = new Mesh
        {
            name = "OutlineProxy_RuntimeMeshMirror"
        };
        ownedMirrorMesh.MarkDynamic();
    }

    private bool TryCopyMesh(Mesh source, Mesh target)
    {
        if (source == null || target == null)
        {
            lastFailureReason = "Source or target runtime mesh is null.";
            return false;
        }

        try
        {
            target.Clear(false);

            source.GetVertices(_bufVertices);
            target.SetVertices(_bufVertices);
            source.GetUVs(0, _bufUV);
            target.SetUVs(0, _bufUV);
            source.GetUVs(1, _bufUV2);
            target.SetUVs(1, _bufUV2);
            source.GetColors(_bufColors);
            target.SetColors(_bufColors);
            source.GetNormals(_bufNormals);
            target.SetNormals(_bufNormals);
            source.GetTangents(_bufTangents);
            target.SetTangents(_bufTangents);

            target.subMeshCount = source.subMeshCount;
            for (int i = 0; i < source.subMeshCount; i++)
            {
                source.GetTriangles(_bufTriangles, i);
                target.SetTriangles(_bufTriangles, i, true);
            }

            target.bounds = source.bounds;
            return true;
        }
        catch (System.Exception ex)
        {
            lastFailureReason = "Copy Mesh failed: " + ex.GetType().Name + " - " + ex.Message;
            return false;
        }
    }

    private void DestroyOwnedMirrorMesh()
    {
        if (ownedMirrorMesh == null)
            return;

        if (Application.isPlaying)
            Destroy(ownedMirrorMesh);
        else
            DestroyImmediate(ownedMirrorMesh);

        ownedMirrorMesh = null;
    }

    private SkeletonAnimation FindBestSourceSkeletonAnimation()
    {
        Transform cursor = transform.parent;

        while (cursor != null)
        {
            SkeletonAnimation[] candidates = cursor.GetComponentsInChildren<SkeletonAnimation>(true);
            SkeletonAnimation best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < candidates.Length; i++)
            {
                SkeletonAnimation candidate = candidates[i];
                if (candidate == null)
                    continue;

                if (candidate.transform == transform || candidate.GetComponent<SpineOutlineFollower>() == this)
                    continue;

                if (IsProxyLike(candidate.transform))
                    continue;

                MeshFilter mf = candidate.GetComponent<MeshFilter>();
                if (mf == null)
                    continue;

                int score = ScoreSourceCandidate(candidate.transform);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            if (best != null)
                return best;

            cursor = cursor.parent;
        }

        return null;
    }

    private static int ScoreSourceCandidate(Transform t)
    {
        string path = GetPath(t).ToLowerInvariant();
        int score = 0;

        if (path.Contains("visual")) score += 20;
        if (path.Contains("spine")) score += 16;
        if (path.Contains("render")) score += 8;
        if (path.Contains("body")) score += 4;
        if (path.Contains("axia")) score += 4;

        if (path.Contains("outline")) score -= 100;
        if (path.Contains("proxy")) score -= 100;
        if (path.Contains("shadow")) score -= 80;
        if (path.Contains("occlusion")) score -= 80;
        if (path.Contains("silhouette")) score -= 40;

        return score;
    }

    private static bool IsProxyLike(Transform t)
    {
        if (t == null)
            return false;

        string path = GetPath(t).ToLowerInvariant();
        return path.Contains("outline")
            || path.Contains("proxy")
            || path.Contains("shadow")
            || path.Contains("occlusion")
            || path.Contains("silhouette");
    }

    private static void CopyWorldTransform(Transform sourceTransform, Transform targetTransform)
    {
        if (sourceTransform == null || targetTransform == null)
            return;

        targetTransform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);
        SetLossyScale(targetTransform, sourceTransform.lossyScale);
    }

    private static void SetLossyScale(Transform targetTransform, Vector3 desiredWorldScale)
    {
        if (targetTransform == null)
            return;

        Transform parent = targetTransform.parent;
        if (parent == null)
        {
            targetTransform.localScale = desiredWorldScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;
        targetTransform.localScale = new Vector3(
            SafeDivide(desiredWorldScale.x, parentScale.x),
            SafeDivide(desiredWorldScale.y, parentScale.y),
            SafeDivide(desiredWorldScale.z, parentScale.z));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
    }

    private static string GetCurrentAnimationName(SkeletonAnimation skeletonAnimation)
    {
        if (skeletonAnimation == null)
            return "<none>";

        string animationName = skeletonAnimation.AnimationName;
        return string.IsNullOrWhiteSpace(animationName) ? "<none>" : animationName;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "<none>";

        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }
        return path;
    }
}
