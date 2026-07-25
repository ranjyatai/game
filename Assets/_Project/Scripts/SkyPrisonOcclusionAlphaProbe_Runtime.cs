using UnityEngine;

/// <summary>
/// 临时调试用：强制把当前物体以及子物体下所有 Renderer 的
/// _SkyPrison_OcclusionAlpha 写入 MaterialPropertyBlock。
///
/// 用法：
/// 1. 挂到 Player / VisualRoot / SpineRoot 任意父节点。
/// 2. Play 后调 inspector 里的 alpha。
/// 3. 如果本体和黄描边都会同步透明，说明 Shader 没问题，是遮挡脚本没有写到所有 Renderer。
/// 4. 如果某些部分完全不变，Console 会提示那些材质没有 _SkyPrison_OcclusionAlpha 属性，说明它们用的不是这次改过的 Shader。
/// </summary>
[ExecuteAlways]
public class SkyPrisonOcclusionAlphaProbe : MonoBehaviour
{
    private static readonly int OcclusionAlphaId = Shader.PropertyToID("_SkyPrison_OcclusionAlpha");

    [Range(0f, 1f)]
    public float alpha = 1f;

    public bool applyEveryFrame = true;
    public bool logMaterials = false;

    private MaterialPropertyBlock propertyBlock;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void LateUpdate()
    {
        if (applyEveryFrame)
            Apply();
    }

    [ContextMenu("Apply Occlusion Alpha Now")]
    public void Apply()
    {
        if (propertyBlock == null)
            propertyBlock = new MaterialPropertyBlock();

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(OcclusionAlphaId, alpha);
            renderer.SetPropertyBlock(propertyBlock);

            if (logMaterials)
                LogRendererMaterials(renderer);
        }
    }

    [ContextMenu("Reset To Full Visible")]
    public void ResetToFullVisible()
    {
        alpha = 1f;
        Apply();
    }

    private void LogRendererMaterials(Renderer renderer)
    {
        Material[] materials = renderer.sharedMaterials;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
            {
                Debug.LogWarning("[OcclusionAlphaProbe] Null material: " + GetPath(renderer.transform), renderer);
                continue;
            }

            Shader shader = material.shader;
            bool hasProperty = material.HasProperty(OcclusionAlphaId);

            string message =
                "[OcclusionAlphaProbe] " +
                GetPath(renderer.transform) +
                " / material[" + i + "]=" + material.name +
                " / shader=" + (shader != null ? shader.name : "NULL") +
                " / has _SkyPrison_OcclusionAlpha=" + hasProperty;

            if (hasProperty)
                Debug.Log(message, renderer);
            else
                Debug.LogWarning(message, renderer);
        }
    }

    private static string GetPath(Transform transform)
    {
        if (transform == null)
            return "";

        string path = transform.name;
        Transform parent = transform.parent;

        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
}
