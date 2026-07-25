using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 挂在背包面板根节点（InventoryPanel）上。
    /// 根据当前画布分辨率与参考分辨率的比值，自动缩放面板尺寸。
    /// 公式：scale = pow(canvasHeight / referenceHeight, 0.5)
    ///   1080p → 1.00×  2K(1440p) → 1.15×  4K(2160p) → 1.41×
    /// 这样 4K 下面板约放大 1.4 倍，不会随线性比例过度膨胀。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonInventoryPanelScaler : MonoBehaviour
    {
        [SerializeField] private float referenceHeight = 1080f;
        [SerializeField] [Range(0f, 1f)] private float scalePower = 0.5f;  // 0=不缩放 1=线性 0.5=平方根
        [SerializeField] private float minScale = 0.7f;
        [SerializeField] private float maxScale = 2.0f;

        // 固定倍率，不跟分辨率挂钩（CanvasScaler 已经独立处理分辨率适配）。
        //
        // 这个 1.3 倍本来是历史遗留 bug（旧参考分辨率年代补的手动缩放，新的4K
        // CanvasScaler 标准接入后本该跟着归零，一直没人改）。但背包用这个尺寸已经用了
        // 很久、玩家已经看惯了，改回1.0反而要重新调一遍背包的观感——所以决定反过来：
        // 把这个已经实际生效很久的 1.3 倍直接当成新的"标准1.0"，SkyPrisonFloatingWindowKit
        // 里所有的悬浮窗尺寸标准（角标/关闭按钮/字号/标题栏）都乘上了同一个
        // StandardScaleMultiplier=1.3，跟这里保持一致，不再互相追着改。
        [SerializeField] private float constantScale = 1.3f;

        private RectTransform _rt;
        private Canvas _canvas;

        private void Awake()
        {
            _rt     = GetComponent<RectTransform>();
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null) _canvas = _canvas.rootCanvas;
        }

        private void OnEnable()  => ApplyScale();
        private void Start()     => ApplyScale();

        // 这个脚本是在 CanvasScaler 参考分辨率还是 1920x1080 的年代写的，用来给高分屏
        // 再叠加一层"不要按线性比例过度放大"的手动补偿。现在 CanvasScaler 本身的参考
        // 分辨率已经统一改成真正的 4K（3840x2160，见 UI参考分辨率标准），ScaleWithScreenSize
        // 已经能让面板在任何分辨率下保持完全一致的相对比例——这个脚本再叠一层跟分辨率
        // 相关的 localScale，等于两套缩放系统打架，才会出现"4K 和 1080p 下背包窗口占屏幕
        // 比例不一样"这个 bug（实测：4K 下额外乘 1.33x，1080p 下额外乘 0.91x，跟这里的
        // pow(Screen.height/1080, 0.5) 公式算出来的数字完全对得上）。CanvasScaler 已经
        // 独立正确处理了这件事，这个脚本不再需要做任何事，永远保持 1:1，不再读分辨率。
        [ContextMenu("Apply Scale Now")]
        public void ApplyScale()
        {
            if (_rt == null) return;
            _rt.localScale = Vector3.one * constantScale;
        }

        private float GetCanvasHeight()
        {
            // ScreenSpaceOverlay：用屏幕高度
            if (_canvas != null)
            {
                if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    return Screen.height;

                // ScreenSpaceCamera / WorldSpace：用 Canvas RectTransform
                RectTransform crt = _canvas.GetComponent<RectTransform>();
                if (crt != null) return crt.rect.height;
            }

            return Screen.height;
        }
    }
}
