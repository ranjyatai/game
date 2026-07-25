using TMPro;
using UnityEngine;

/// <summary>
/// Applies Illustrator-like horizontal / vertical glyph deformation to TMP text.
/// This does not scale the RectTransform, so anchors, positions and layout boxes stay stable.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class SkyPrisonTMPGlyphAspect : MonoBehaviour
{
    [SerializeField] private float horizontalAspect = 1f;
    [SerializeField] private float verticalAspect = 1f;

    private TMP_Text targetText;
    private bool dirty = true;
    private string lastText;
    private float lastHorizontal;
    private float lastVertical;

    public float HorizontalAspect => horizontalAspect;
    public float VerticalAspect => verticalAspect;

    /// <summary>
    /// Immediately rebuilds and reapplies glyph deformation.
    /// Used by the HUD workbench after forcing TMP mesh refresh so the preview cannot display
    /// one frame of the undeformed/original TMP mesh.
    /// </summary>
    public void ApplyNow()
    {
        CacheText();
        if (targetText == null)
            return;

        ApplyGlyphAspect();
    }

    public void SetAspect(Vector2 aspect)
    {
        float h = NormalizeAspect(aspect.x);
        float v = NormalizeAspect(aspect.y);

        if (Mathf.Approximately(horizontalAspect, h) && Mathf.Approximately(verticalAspect, v))
            return;

        horizontalAspect = h;
        verticalAspect = v;
        MarkDirty();
    }

    private void Awake()
    {
        CacheText();
    }

    private void OnEnable()
    {
        CacheText();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        MarkDirty();
    }

    private void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        CacheText();
        if (targetText != null)
        {
            targetText.ForceMeshUpdate(false, false);
            targetText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);
        }
    }

    private void OnValidate()
    {
        horizontalAspect = NormalizeAspect(horizontalAspect);
        verticalAspect = NormalizeAspect(verticalAspect);
        MarkDirty();
    }

    private void LateUpdate()
    {
        CacheText();
        if (targetText == null)
            return;

        bool changed = dirty
            || lastText != targetText.text
            || !Mathf.Approximately(lastHorizontal, horizontalAspect)
            || !Mathf.Approximately(lastVertical, verticalAspect);

        // In edit/workbench preview TMP may rebuild its own mesh after our previous deformation.
        // If we only react to text/aspect value changes, the preview can alternate between
        // the original glyph width and the deformed glyph width.
        // Keep edit-mode preview WYSIWYG by reapplying after TMP every editor frame.
        if (!changed && Application.isPlaying)
            return;

        ApplyGlyphAspect();
    }

    private void OnTextChanged(Object changedObject)
    {
        if (changedObject == targetText)
            MarkDirty();
    }

    private void CacheText()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();
    }

    private void MarkDirty()
    {
        dirty = true;
    }

    private void ApplyGlyphAspect()
    {
        horizontalAspect = NormalizeAspect(horizontalAspect);
        verticalAspect = NormalizeAspect(verticalAspect);

        // Rebuild original glyph vertices first, otherwise deformation would accumulate.
        targetText.ForceMeshUpdate(false, false);

        TMP_TextInfo textInfo = targetText.textInfo;
        if (textInfo == null)
            return;

        bool identity = Mathf.Approximately(horizontalAspect, 1f) && Mathf.Approximately(verticalAspect, 1f);
        if (!identity)
        {
            for (int i = 0; i < textInfo.characterCount; i++)
            {
                TMP_CharacterInfo ch = textInfo.characterInfo[i];
                if (!ch.isVisible)
                    continue;

                int materialIndex = ch.materialReferenceIndex;
                int vertexIndex = ch.vertexIndex;
                if (materialIndex < 0 || materialIndex >= textInfo.meshInfo.Length)
                    continue;

                Vector3[] vertices = textInfo.meshInfo[materialIndex].vertices;
                if (vertices == null || vertexIndex + 3 >= vertices.Length)
                    continue;

                Vector3 center = (vertices[vertexIndex + 0] + vertices[vertexIndex + 2]) * 0.5f;
                ScaleVertex(ref vertices[vertexIndex + 0], center);
                ScaleVertex(ref vertices[vertexIndex + 1], center);
                ScaleVertex(ref vertices[vertexIndex + 2], center);
                ScaleVertex(ref vertices[vertexIndex + 3], center);
            }
        }

        for (int i = 0; i < textInfo.meshInfo.Length; i++)
        {
            TMP_MeshInfo meshInfo = textInfo.meshInfo[i];
            if (meshInfo.mesh == null)
                continue;

            meshInfo.mesh.vertices = meshInfo.vertices;
            targetText.UpdateGeometry(meshInfo.mesh, i);
        }

        targetText.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices);

        lastText = targetText.text;
        lastHorizontal = horizontalAspect;
        lastVertical = verticalAspect;
        dirty = false;
    }

    private void ScaleVertex(ref Vector3 vertex, Vector3 center)
    {
        vertex.x = center.x + (vertex.x - center.x) * horizontalAspect;
        vertex.y = center.y + (vertex.y - center.y) * verticalAspect;
    }

    private static float NormalizeAspect(float value)
    {
        if (value <= 0f)
            value = 1f;
        return Mathf.Clamp(value, 0.35f, 3.00f);
    }
}
