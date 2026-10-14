using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>选关沙盘的滚轮缩放 / 拖动平移：转给沙盘相机（真缩放，不是放大图片）。</summary>
public sealed class SkyPrisonRaidDioramaInput : MonoBehaviour, IScrollHandler, IDragHandler
{
    public SkyPrisonRaidDioramaController target;

    public void OnScroll(PointerEventData e) { if (target != null) target.ZoomBy(Mathf.Exp(-e.scrollDelta.y * 0.08f)); }

    public void OnDrag(PointerEventData e)
    {
        if (target == null) return;
        if (e.button == PointerEventData.InputButton.Left) target.PanByPixels(e.delta);       // 左键拖动：平移
        else if (e.button == PointerEventData.InputButton.Right) target.Orbit(e.delta);        // 右键拖动：略微转动 / 俯仰
    }
}
