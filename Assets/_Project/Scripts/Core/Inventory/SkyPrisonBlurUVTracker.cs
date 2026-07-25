using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 挂在背包面板根节点上。
    /// 每帧根据面板在屏幕上的实际位置，更新 RawImage.uvRect，
    /// 让模糊 RT（全屏截图）正确对齐面板背后的场景区域。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonBlurUVTracker : MonoBehaviour
    {
        [SerializeField] private RawImage      blurImage;
        [SerializeField] private RectTransform panelRect;

        [Tooltip("翻转采样 Y。相机渲染到 RT 通常不需要翻转(保持关)；若磨砂上下颠倒再勾上。")]
        [SerializeField] private bool flipY = false;

        private readonly Vector3[] _corners = new Vector3[4];
        private Canvas _canvas;

        public void Bind(RawImage img, RectTransform panel)
        {
            blurImage = img;
            panelRect = panel;
            _canvas   = null;
        }

        // 关窗压缩动画期间冻结：停止按屏幕角点重算 uvRect，让磨砂图随矩形刚性压缩、不扭曲。
        public bool Frozen { get; set; }

        private void LateUpdate()
        {
            if (Frozen) return;
            if (blurImage == null || panelRect == null) return;
            if (blurImage.texture == null) return;

            if (_canvas == null) _canvas = panelRect.GetComponentInParent<Canvas>();

            // 把面板四角投影成屏幕像素坐标。Overlay 用 null 相机，ScreenSpaceCamera/World 用 Canvas 相机
            // （或主相机兜底）。直接拿 GetWorldCorners 当屏幕坐标只在 Overlay 成立，否则会完全错位。
            Camera cam = null;
            if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = _canvas.worldCamera != null ? _canvas.worldCamera : Camera.main;

            panelRect.GetWorldCorners(_corners);
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]); // 左下
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]); // 右上

            float sw = Mathf.Max(1, Screen.width);
            float sh = Mathf.Max(1, Screen.height);
            float xMin = bl.x / sw, xMax = tr.x / sw;
            float yMin = bl.y / sh, yMax = tr.y / sh;

            // 相机 RT 一般正向，默认不翻转；需要时再翻 Y。
            float vMin = flipY ? 1f - yMax : yMin;
            float vMax = flipY ? 1f - yMin : yMax;

            blurImage.uvRect = new Rect(xMin, vMin, xMax - xMin, vMax - vMin);
        }
    }
}
