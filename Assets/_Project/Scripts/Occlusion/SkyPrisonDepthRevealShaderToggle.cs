// SkyPrisonDepthRevealShaderToggle.cs
// Plain MonoBehaviour, not a ScriptableRendererFeature - sidesteps the RendererFeature
// registration/invocation issues hit while prototyping the RenderGraph-based approach.
//
// V2: no longer computes "is this pixel occluded" via shader-side depth comparison (that
// approach failed - Spine parts get manual Z pushes for draw-order that don't represent real
// position, corrupting any per-vertex/per-pixel depth test). Instead this reads the game's own
// already-correct occlusion determination - SimpleDirectionalOccluder's world-space Z-threshold
// + anchor-point check, which already writes into UnitOcclusionMaterialReceiver.CurrentOccluded
// per unit - and just forwards that boolean to the shader's fill toggle
// (_SP_DepthRevealEnable) via MaterialPropertyBlock. The shader no longer needs to know
// anything about depth; it only draws the reveal color when told to.

using UnityEngine;

public sealed class SkyPrisonDepthRevealShaderToggle : MonoBehaviour
{
    [SerializeField] private string spineOcclusionShaderName = "Spine/SpineOcclusionComposite";
    [SerializeField] private string pathKeywords = "Player;PlayerRuntime;Enemy;Mob;Monster";
    [SerializeField] private Color revealColor = new Color(1f, 0.83f, 0f, 1f);
    [Range(0f, 1f)][SerializeField] private float revealAlpha = 0.6f;
    [SerializeField] private float rescanInterval = 1f;

    [Header("Debug - read only")]
    [SerializeField] private string lastStatus = "-";

    private static readonly int EnableId = Shader.PropertyToID("_SP_DepthRevealEnable");
    private static readonly int ColorId = Shader.PropertyToID("_SP_DepthRevealColor");
    private static readonly int AlphaId = Shader.PropertyToID("_SP_DepthRevealAlpha");

    private float nextScan;

    private void OnEnable()
    {
        nextScan = 0f;
    }

    private void Update()
    {
        if (Time.time < nextScan)
            return;
        nextScan = Time.time + rescanInterval;
        Apply();
    }

    private void Apply()
    {
        string[] keywords = string.IsNullOrWhiteSpace(pathKeywords) ? System.Array.Empty<string>() : pathKeywords.Split(';');

        Renderer[] all = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        int scanned = 0;
        int matched = 0;
        int noReceiver = 0;
        int occluded = 0;

        foreach (Renderer r in all)
        {
            scanned++;
            string path = GetPath(r.transform).ToLowerInvariant();

            bool pathMatched = false;
            for (int i = 0; i < keywords.Length; i++)
            {
                string kw = keywords[i].Trim().ToLowerInvariant();
                if (kw.Length > 0 && path.Contains(kw))
                {
                    pathMatched = true;
                    break;
                }
            }
            if (!pathMatched)
                continue;

            Material[] mats = r.sharedMaterials;
            bool hasShader = false;
            if (mats != null)
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    Material m = mats[i];
                    if (m != null && m.shader != null && m.shader.name == spineOcclusionShaderName)
                    {
                        hasShader = true;
                        break;
                    }
                }
            }
            if (!hasShader)
                continue;

            matched++;

            // The unit's own occlusion truth: SimpleDirectionalOccluder already computed this
            // via world-space Z threshold + anchor points and wrote it here. Do not re-derive it.
            UnitOcclusionMaterialReceiver receiver = r.GetComponentInParent<UnitOcclusionMaterialReceiver>();
            bool isOccluded = receiver != null && receiver.CurrentOccluded;
            if (receiver == null)
                noReceiver++;
            else if (isOccluded)
                occluded++;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(EnableId, isOccluded ? 1f : 0f);
            mpb.SetColor(ColorId, revealColor);
            mpb.SetFloat(AlphaId, revealAlpha);
            r.SetPropertyBlock(mpb);
        }

        lastStatus = "scanned=" + scanned + ", matched=" + matched + ", noReceiver=" + noReceiver + ", occluded=" + occluded;
    }

    private static string GetPath(Transform t)
    {
        if (t == null)
            return "";
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }
}
