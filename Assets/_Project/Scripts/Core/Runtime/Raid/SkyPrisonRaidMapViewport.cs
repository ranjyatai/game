using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 选本地图的视口：滚轮缩放（以光标为中心，带缓动）、按住拖动平移、始终夹在地图边界内（缩放 1 时地图正好铺满屏幕）。
/// 节点反向缩放，不管放大多少倍都保持固定屏幕大小。
/// 用 Unity UI 事件接口（IScrollHandler / IDragHandler）实现，旧输入和新输入系统都能响应。
/// 视口（本组件所在物体）要是全屏、能接收射线的 Graphic；content 是地图本体（中心 pivot / 中心锚点）。
/// </summary>
public sealed class SkyPrisonRaidMapViewport : MonoBehaviour, IDragHandler, IScrollHandler
{
    public RectTransform viewport;
    public RectTransform content;
    public float minScale = 1f, maxScale = 4f;
    [Tooltip("滚轮一格的缩放强度（指数）。")] public float wheelStep = 0.2f;
    [Tooltip("缓动速度，越大越跟手。")] public float smooth = 14f;

    readonly List<RectTransform> keepSize = new List<RectTransform>();
    float scale = 1f, targetScale = 1f;
    Vector2 pivotScreen;
    Canvas canvas;

    public void Register(RectTransform node) { keepSize.Add(node); node.localScale = Vector3.one / scale; }

    void Awake() { canvas = GetComponentInParent<Canvas>(); pivotScreen = new Vector2(Screen.width / 2f, Screen.height / 2f); }

    public void ZoomBy(float factor, Vector2 screenPoint) { targetScale = Mathf.Clamp(targetScale * factor, minScale, maxScale); pivotScreen = screenPoint; }
    public void ZoomButton(float factor) => ZoomBy(factor, new Vector2(Screen.width / 2f, Screen.height / 2f));
    public void ResetView() { targetScale = scale = 1f; content.localScale = Vector3.one; content.anchoredPosition = Vector2.zero; ApplyNodeScale(); }

    public void OnScroll(PointerEventData e)
    {
        // 旧输入一格 = ±1，新输入系统 UI 模块一格可能是 ±几十；触控板是小数。统一成「一格 = 1」。
        float y = e.scrollDelta.y; float d = Mathf.Abs(y) >= 1f ? Mathf.Sign(y) : y;
        ZoomBy(Mathf.Exp(d * wheelStep), e.position);
    }

    public void OnDrag(PointerEventData e)
    {
        float k = canvas != null ? canvas.scaleFactor : 1f;
        content.anchoredPosition += e.delta / Mathf.Max(0.0001f, k);
        Clamp();
    }

    void Update()
    {
        if (Mathf.Approximately(scale, targetScale)) return;
        float next = Mathf.Lerp(scale, targetScale, 1f - Mathf.Exp(-smooth * Time.unscaledDeltaTime));
        if (Mathf.Abs(next - targetScale) < 0.001f) next = targetScale;
        SetScaleAround(next, pivotScreen);
    }

    void SetScaleAround(float s2, Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screenPoint, null, out Vector2 local);   // 视口中心为原点
        Vector2 pos = content.anchoredPosition;
        content.anchoredPosition = local - (local - pos) * (s2 / scale);     // 光标下的那个点保持不动
        scale = s2; content.localScale = new Vector3(s2, s2, 1f);
        Clamp(); ApplyNodeScale();
    }

    void Clamp()
    {
        Vector2 half = content.rect.size * scale / 2f, vh = viewport.rect.size / 2f;
        float mx = Mathf.Max(0f, half.x - vh.x), my = Mathf.Max(0f, half.y - vh.y);
        Vector2 p = content.anchoredPosition;
        content.anchoredPosition = new Vector2(Mathf.Clamp(p.x, -mx, mx), Mathf.Clamp(p.y, -my, my));
    }

    void ApplyNodeScale() { Vector3 inv = Vector3.one / scale; foreach (var n in keepSize) if (n != null) n.localScale = inv; }
}
