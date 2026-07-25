using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 选中标签底部的绿色细线指示条。
    ///
    /// 2026-07-23 重写两次：第一次把"离屏相机渲文字+多级模糊"那套不稳定的实时渲染
    /// 管线换成了纯 UI.Image 摆放（不再有摄像机/RT/模糊shader，结果完全确定）；
    /// 用户看过之后进一步要求简化成单条绿色细线，并且切换标签时要平滑滑过去，
    /// 不能直接瞬间跳过去——这次补上"连续滑动"这一步。
    ///
    /// 关键点：细线固定挂在"这个组件自己所在的节点"（也就是背包/仓库窗口的根面板，
    /// 是所有筛选标签共同的稳定祖先），不再像旧版那样每次都重新 parent 到"当前选中
    /// 标签"自己的父节点下——不同标签的父节点是各自独立的 FilterTab_xxx 节点，
    /// 局部坐标系不一样，如果每次都换父节点再在新坐标系里 Lerp，走出来的路径根本
    /// 不对（会先跳到新坐标系原点附近再滑，而不是真的横向滑过去）。用世界坐标把
    /// 目标标签的中心点换算成"这个稳定父节点"下的本地坐标，才能连续、正确地滑动。
    ///
    /// 保留跟旧版完全相同的公开接口（Configure/ShowFor/Hide），背包
    /// InventoryWindowController 和仓库 StashWindowController 都不用改一行调用代码。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonTabGlowRenderer : MonoBehaviour
    {
        // font/intensity/padding 参数保留纯粹是为了不改调用方的 Configure 签名——
        // 这个新实现是单色细线，用不上字体渲染，intensity 只用来整体缩放透明度。
        private Color _lineColor = new Color(0.42f, 0.92f, 0.68f, 1f);
        private float _alphaScale = 1f;

        private RectTransform _displayRoot;
        private RectTransform _stableParent; // 稳定祖先——这个组件自己所在的 RectTransform
        private Image _line;

        private const float LineHeight = 3f;
        // 之前用 Vector3.Lerp(cur,target,dt*rate) 这种"每帧走完剩余距离的固定比例"的
        // 指数衰减插值——收敛所需的帧数只取决于 rate，跟距离本身无关，这意味着距离越
        // 远，前一两帧就要把绝大部分路程走完，帧间位移大到肉眼看起来像"瞬间跳过去，
        // 最后一点点才是在滑"，隔一个以上标签的跳转正是这个原因。改成"每秒移动多少
        // 像素"的匀速移动(Vector3.MoveTowards)，帧间位移不再随距离暴涨。
        //
        // BaseSpeed 是相邻标签(短距离)已经验证过手感不错的速度；隔的标签越多，距离
        // 越远，如果还用这个固定速度总时长会拖得很长，所以再叠加"距离越远速度越快"
        // 这一条——但不能无限快，用 MaxDuration 卡一个上限，保证不管跳多远，最终都会
        // 在这个时间内跑完，两者取较大值。
        private const float BaseSpeed  = 900f;
        private const float MaxDuration = 0.22f;

        private bool _visible;
        private Vector2 _targetLocalPos;
        private float _targetWidth;
        private float _moveSpeed  = BaseSpeed;
        private float _widthSpeed = BaseSpeed;

        // padding 参数不再使用——线宽现在直接等于类别框宽度（见 ShowFor），不是"文字宽度+间距"，
        // 保留这个参数纯粹是为了不改调用方的 Configure 签名。
        public void Configure(TMP_FontAsset font, Color glowColor, float intensity, float padding)
        {
            _lineColor = glowColor;
            _alphaScale = Mathf.Clamp01(intensity / 1.5f); // 1.5 是旧版 glowIntensity 的默认值，保持强度感觉一致
            EnsureDisplay();
        }

        public void Hide()
        {
            _visible = false;
            if (_displayRoot != null) _displayRoot.gameObject.SetActive(false);
        }

        /// <summary>给指定标签显示底部细线，会平滑滑到新位置，不是瞬间跳过去。</summary>
        public void ShowFor(TextMeshProUGUI label)
        {
            if (label == null) { Hide(); return; }
            EnsureDisplay();

            // 标签是 StretchFull 铺满整个类别框(Tab)的，所以 label.rectTransform 自己的
            // rect 宽高就是"这个类别框"本身的宽高——不再用 GetRenderedValues 量文字实际
            // 渲染宽度(那样线会比框窄一截，只贴着文字)，线宽直接等于类别框宽度。
            RectTransform lrt = label.rectTransform;
            float lineWidth = lrt.rect.width;

            // 标签自身 rect 中心的局部坐标（pivot → center 的偏移），再整体换算成世界坐标。
            Vector2 pivotToCenter = new Vector2(
                (0.5f - lrt.pivot.x) * lrt.rect.width,
                (0.5f - lrt.pivot.y) * lrt.rect.height);
            float underlineLocalY = -lrt.rect.height * 0.5f + 6f; // 贴着类别框底边，往上留一点点间隙
            Vector3 worldPoint = lrt.TransformPoint(new Vector3(pivotToCenter.x, pivotToCenter.y + underlineLocalY, 0f));

            // 世界坐标转换成"稳定祖先"下的本地坐标——不管当前选中的是哪个标签(各自
            // 父节点都不一样)，换算完之后都是同一个坐标系，才能连续滑动。
            Vector3 localInStableParent = _stableParent.InverseTransformPoint(worldPoint);
            _targetLocalPos = new Vector2(localInStableParent.x, localInStableParent.y);
            _targetWidth = lineWidth;

            bool firstShow = !_visible;
            _visible = true;
            _displayRoot.gameObject.SetActive(true);

            if (firstShow)
            {
                // 窗口刚打开、第一次显示：直接吸附到位，不用从屏幕外飞一段没有意义的距离。
                _displayRoot.localPosition = new Vector3(_targetLocalPos.x, _targetLocalPos.y, 0f);
                SetWidth(_targetWidth);
                _moveSpeed = BaseSpeed;
                _widthSpeed = BaseSpeed;
            }
            else
            {
                // 按"这一趟距离 / MaxDuration"和 BaseSpeed 取较大值算出这一趟专用的速度——
                // 相邻标签距离短，distance/MaxDuration 小于 BaseSpeed，走的还是原来验证过
                // 手感的固定速度；隔得越远，distance/MaxDuration 越大，速度跟着线性提高，
                // 但总时长永远不会超过 MaxDuration，不会出现"跳得越远等得越久"的感觉。
                float posDistance = Vector2.Distance(
                    new Vector2(_displayRoot.localPosition.x, _displayRoot.localPosition.y), _targetLocalPos);
                _moveSpeed = Mathf.Max(BaseSpeed, posDistance / MaxDuration);

                float widthDistance = Mathf.Abs(_targetWidth - _line.rectTransform.sizeDelta.x);
                _widthSpeed = Mathf.Max(BaseSpeed, widthDistance / MaxDuration);
            }

            Color c = _lineColor;
            c.a = 0.95f * _alphaScale;
            _line.color = c;
        }

        // Time.unscaledDeltaTime 本身逐帧会有细微抖动——角色站着不动时整个场景没什么在
        // 算，CPU/GPU容易出现频率/功耗状态切换，帧间隔反而比走动时更不均匀；这条下划线
        // 是画面上唯一持续在动的东西，任何一点点dt抖动都会被放大成肉眼可见的"不丝滑"，
        // 角色走动时因为整体渲染负载更饱和、帧间隔更稳定，同样量级的抖动反而看不出来。
        // 用一个简单的指数滑动平均抹掉逐帧dt抖动，不直接拿原始dt算步长。
        private float _smoothedDt = 1f / 60f;

        private void Update()
        {
            if (!_visible) return;

            Vector3 cur = _displayRoot.localPosition;
            Vector3 target = new Vector3(_targetLocalPos.x, _targetLocalPos.y, cur.z);
            float curW = _line.rectTransform.sizeDelta.x;

            // 已经到位就什么都不写——之前这里每帧都无条件重新赋值 localPosition/sizeDelta，
            // 哪怕数值跟上一帧完全一样，也照样触发一次 Canvas 重建。这个组件常驻在窗口打开
            // 期间，等于只要窗口开着，不管有没有在切标签，每一帧都在白白多一次UI重建；
            // 这条线又是背包/仓库窗口面板上唯一持续这样写的东西，跟负重条(它自己有
            // Approximately提前退出)不一样。加回收敛判断，到位之后彻底不再写。
            bool posDone = (cur - target).sqrMagnitude < 0.01f;
            bool widthDone = Mathf.Abs(curW - _targetWidth) < 0.01f;
            if (posDone && widthDone) return;

            _smoothedDt = Mathf.Lerp(_smoothedDt, Time.unscaledDeltaTime, 0.15f);
            float dt = _smoothedDt;

            if (!posDone)
                _displayRoot.localPosition = Vector3.MoveTowards(cur, target, _moveSpeed * dt);
            if (!widthDone)
                SetWidth(Mathf.MoveTowards(curW, _targetWidth, _widthSpeed * dt));
        }

        private void SetWidth(float width)
        {
            _line.rectTransform.sizeDelta = new Vector2(width, LineHeight);
        }

        private void EnsureDisplay()
        {
            if (_displayRoot != null) return;

            _stableParent = (RectTransform)transform;

            var rootGo = new GameObject("TabUnderline", typeof(RectTransform));
            _displayRoot = rootGo.GetComponent<RectTransform>();
            _displayRoot.SetParent(_stableParent, false);
            // 用 localPosition（不是 anchoredPosition）定位——anchorMin/Max/pivot 在这里
            // 只是随便选一个点锚点，值本身不影响定位逻辑，因为全程用 Transform.localPosition
            // 配合 InverseTransformPoint 计算，不走 RectTransform 的锚点/rect 那套语义
            // (稳定父节点自己的 pivot 五花八门——背包是 (1,0.5)、仓库是 (0.5,0.5)，
            // 如果用 anchoredPosition 配合 anchorMin=(0,0) 会因为这个 pivot 差异算错位置)。
            _displayRoot.anchorMin = _displayRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _displayRoot.pivot = new Vector2(0.5f, 0.5f);
            // 这个稳定父节点(窗口根面板)通常没有 LayoutGroup，但保险起见还是忽略布局，
            // 避免万一被当成排版元素挤变形。
            rootGo.AddComponent<LayoutElement>().ignoreLayout = true;
            rootGo.transform.SetAsLastSibling();

            var lineGo = new GameObject("Line", typeof(RectTransform));
            var lineRt = (RectTransform)lineGo.transform;
            lineRt.SetParent(_displayRoot, false);
            lineRt.anchorMin = lineRt.anchorMax = new Vector2(0.5f, 0.5f);
            lineRt.pivot = new Vector2(0.5f, 0.5f);
            _line = lineGo.AddComponent<Image>();
            _line.raycastTarget = false;

            rootGo.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_displayRoot != null) Destroy(_displayRoot.gameObject);
        }
    }
}
