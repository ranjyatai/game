using UnityEngine;

/// <summary>
/// Formal render-resource dependency for Sky Prison screen-space occlusion.
///
/// Purpose:
/// - Keep Hidden/SkyPrison/UnitHiddenMaskComposite reachable through a serialized asset chain.
/// - Avoid runtime Shader.Find dependency for Build / Play parity.
/// - Make missing render resources visible and fixable from the scene, not hidden in code.
///
/// Expected chain:
/// ScreenSpaceOutlineSystem
///   -> SkyPrisonOcclusionRenderResources.asset
///      -> M_UnitHiddenMaskComposite.mat
///         -> Hidden/SkyPrison/UnitHiddenMaskComposite
/// </summary>
[CreateAssetMenu(
    fileName = "SkyPrisonOcclusionRenderResources",
    menuName = "Sky Prison/Occlusion/Occlusion Render Resources")]
public sealed class SkyPrisonOcclusionRenderResources : ScriptableObject
{
    [Header("Unit Hidden Mask Composite")]
    [SerializeField] private Material unitHiddenMaskCompositeMaterial;
    [SerializeField] private Shader unitHiddenMaskCompositeShader;

    [Header("Optional Overlay Base")]
    [SerializeField] private Material outlineOverlayBaseMaterial;

    public Material UnitHiddenMaskCompositeMaterial => unitHiddenMaskCompositeMaterial;
    public Shader UnitHiddenMaskCompositeShader => unitHiddenMaskCompositeShader;
    public Material OutlineOverlayBaseMaterial => outlineOverlayBaseMaterial;

    public bool HasCompositeMaterialOrShader =>
        unitHiddenMaskCompositeMaterial != null || unitHiddenMaskCompositeShader != null;
}
