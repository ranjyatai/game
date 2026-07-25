#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class SkyPrisonOcclusionAlphaProbeMenu
{
    [MenuItem("Tools/Sky Prison/Debug/给选中物体添加遮挡透明测试器")]
    public static void AddProbeToSelection()
    {
        GameObject go = Selection.activeGameObject;
        if (go == null)
        {
            Debug.LogWarning("[OcclusionAlphaProbe] 请先在 Hierarchy 里选中 Player / VisualRoot / SpineRoot。");
            return;
        }

        SkyPrisonOcclusionAlphaProbe probe = go.GetComponent<SkyPrisonOcclusionAlphaProbe>();
        if (probe == null)
            probe = go.AddComponent<SkyPrisonOcclusionAlphaProbe>();

        probe.alpha = 0.25f;
        probe.applyEveryFrame = true;
        probe.logMaterials = true;
        probe.Apply();

        EditorUtility.SetDirty(go);
        Debug.Log("[OcclusionAlphaProbe] 已添加到: " + go.name + "。Play 后调 alpha 看本体和黄描边是否一起透明。", go);
    }
}
#endif
