using TMPro;
using UnityEngine;

/// <summary>
/// 挂到 TMP_Text 上，每帧对指定可见字符范围做流动彩虹渐变。
/// 范围外的字符保持原色（由富文本 tag 控制）。
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class RainbowTextEffect : MonoBehaviour
{
    [Tooltip("色相流动速度")]
    [SerializeField] private float speed      = 0.18f;
    [Tooltip("相邻字符的色相偏移")]
    [SerializeField] private float spread     = 0.08f;
    [Tooltip("饱和度")]
    [SerializeField] private float saturation = 0.35f;
    [Tooltip("明度")]
    [SerializeField] private float brightness = 1.0f;

    // 可见字符范围（-1 表示全部）
    private int _visibleStart = -1;
    private int _visibleEnd   = -1;

    // 范围外字符的颜色（默认灰白）
    private Color32 _outsideColor = new Color32(180, 178, 169, 255);

    private TMP_Text _tmp;

    private void Awake()    => _tmp = GetComponent<TMP_Text>();
    private void OnEnable() { if (_tmp == null) _tmp = GetComponent<TMP_Text>(); }

    /// <summary>只对可见字符中第 start..end 范围（含）着彩虹，其余用 outsideColor。</summary>
    public void SetVisibleRange(int start, int end, Color32 outsideColor)
    {
        _visibleStart = start;
        _visibleEnd   = end;
        _outsideColor = outsideColor;
    }

    /// <summary>所有可见字符都着彩虹。</summary>
    public void SetFullRange()
    {
        _visibleStart = -1;
        _visibleEnd   = -1;
    }

    private void Update()
    {
        _tmp.ForceMeshUpdate();
        TMP_TextInfo info = _tmp.textInfo;
        if (info.characterCount == 0) return;

        float baseHue   = Mathf.Repeat(Time.time * speed, 1f);
        int   visibleCi = 0;   // 只计可见字符的序号

        for (int ci = 0; ci < info.characterCount; ci++)
        {
            TMP_CharacterInfo charInfo = info.characterInfo[ci];
            if (!charInfo.isVisible) continue;

            int meshIdx = charInfo.materialReferenceIndex;
            int vIdx    = charInfo.vertexIndex;
            Color32[] verts = info.meshInfo[meshIdx].colors32;

            bool inRange = _visibleStart < 0
                        || (visibleCi >= _visibleStart && visibleCi <= _visibleEnd);

            Color32 c32;
            if (inRange)
            {
                Color c = Color.HSVToRGB(
                    Mathf.Repeat(baseHue + visibleCi * spread, 1f),
                    saturation, brightness);
                c32 = c;
            }
            else
            {
                c32 = _outsideColor;
            }

            verts[vIdx]     = c32;
            verts[vIdx + 1] = c32;
            verts[vIdx + 2] = c32;
            verts[vIdx + 3] = c32;

            visibleCi++;
        }

        for (int m = 0; m < info.meshInfo.Length; m++)
        {
            Mesh mesh = info.meshInfo[m].mesh;
            if (mesh == null) continue;
            mesh.colors32 = info.meshInfo[m].colors32;
            _tmp.UpdateGeometry(mesh, m);
        }
    }

    private void OnDisable()
    {
        if (_tmp == null) return;
        _tmp.color = Color.white;
        _tmp.ForceMeshUpdate();
    }
}
