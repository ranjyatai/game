using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 自定义鼠标图标——系统硬件光标配合 CursorLockMode.Confined 只会把"热点"这一个
    /// 坐标点卡在窗口范围内，完全不知道光标图案实际有多大、往哪个方向延伸，热点卡在
    /// 边缘时图案剩下的部分必然会露出窗口外（或者被系统裁切掉看不见），这是硬件光标
    /// 方案的天生限制，不是数值没调好。
    ///
    /// 解决办法：把系统光标整个隐藏，换成一张跟着鼠标位置走的UI图片，每帧把这张图的
    /// 整个矩形范围（不是一个点）卡在屏幕内——这样图案本身多大、往哪边延伸都不会露出/
    /// 被裁切。
    ///
    /// 配置读的是"界面设置"窗口编辑的 MainMenuSettings（Resources/MainMenuSettings.asset，
    /// 全局可加载，不受场景限制），跟主菜单LOGO/BGM那些字段用的是同一份资源，不用
    /// 单独再建一套配置入口。
    ///
    /// 图标用固定像素大小（ConstantPixelSize），不跟着分辨率/UI整体缩放等比放大——
    /// 跟系统鼠标一样，任何分辨率下看起来都是同样大小，这是刻意的，别改成
    /// ScaleWithScreenSize。
    ///
    /// 自己独立用 [RuntimeInitializeOnLoadMethod] 初始化，不通过
    /// SkyPrisonRuntimeSystemsBootstrapper——那个启动器有一份"跳过场景"名单
    /// （MainMenu/LoadingScene，专门跳过主菜单不初始化游戏内系统），鼠标图标理应
    /// 哪个场景都要显示，不该被那份游戏内系统专用的名单卡住。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkyPrisonCustomCursor : MonoBehaviour
    {
        private const string SettingsResourcePath = "MainMenuSettings";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (!Application.isPlaying) return;
            EnsureInScene();
        }

        private static SkyPrisonCustomCursor _instance;

        private Canvas _canvas;
        private RectTransform _iconRt;
        private Image _icon;

        private MainMenuSettings _settings;
        private float _searchCooldown;
        private const float SearchInterval = 1.5f;

        // 背包这类窗口的色收差是"定期截屏叠加"方案（SkyPrisonInventoryChromatic），
        // 会把当前整个画面截下来做偏色叠加层——截图那一刻鼠标图标的位置会被"烤"进
        // 快照，之后快照一直显示到下次刷新，跟实时移动的真实光标叠在一起就变成残影。
        // 截图前调这个把图标暂时藏起来，截完再调回来，跟那边"截图前隐藏自己的叠加层"
        // 是同一个道理。
        private bool _suspendedForCapture;
        public static void SetSuspendedForCapture(bool suspended)
        {
            if (_instance == null) return;
            _instance._suspendedForCapture = suspended;
            // 直接改，不等下一次Update才生效——调用方通常紧接着就是
            // WaitForEndOfFrame然后截屏，必须在同一帧、这次调用当下就实际隐藏，
            // 不能拖到下一帧才反映到 Image.enabled 上。
            if (_instance._icon != null) _instance._icon.enabled = !suspended;
        }

        private void OnEnable()
        {
            _instance = this;
            Application.focusChanged += OnApplicationFocusChanged;
            TryLoadSettings();
        }

        private void OnDisable()
        {
            Application.focusChanged -= OnApplicationFocusChanged;
            RestoreSystemCursor();
        }

        private void OnApplicationFocusChanged(bool focused)
        {
            // 切出窗口时把系统光标还回去，不然切到桌面/其它窗口鼠标会消失，不知道点哪。
            if (!focused) RestoreSystemCursor();
        }

        private void TryLoadSettings()
        {
            _settings = Resources.Load<MainMenuSettings>(SettingsResourcePath);
        }

        private void Update()
        {
            if (_settings == null || _settings.cursorIconSprite == null)
            {
                _searchCooldown -= Time.unscaledDeltaTime;
                if (_searchCooldown <= 0f) { TryLoadSettings(); _searchCooldown = SearchInterval; }

                if (_settings == null || _settings.cursorIconSprite == null)
                {
                    RestoreSystemCursor();
                    return;
                }
            }

            EnsureCanvas();

            if (_suspendedForCapture)
            {
                _icon.enabled = false;
                return;
            }
            _icon.enabled = true;

            Sprite sprite = _settings.cursorIconSprite;
            if (_icon.sprite != sprite)
                _icon.sprite = sprite;

            float size = Mathf.Max(1f, _settings.cursorIconSize);
            if (_iconRt.sizeDelta.x != size)
                _iconRt.sizeDelta = new Vector2(size, size);

            if (!Application.isFocused)
            {
                RestoreSystemCursor();
                return;
            }

            if (Cursor.visible) Cursor.visible = false;

            PositionIcon(size);
        }

        private void PositionIcon(float size)
        {
            // ConstantPixelSize（scaleFactor固定=1）下，Canvas坐标系跟屏幕像素是
            // 1:1的，Input.mousePosition不用再除任何缩放系数。
            Vector2 mouseInCanvas = Input.mousePosition;

            // 热点偏移：热点在图标里(0,0)=左上角、(1,1)=右下角——这是图片坐标习惯
            // (Y轴朝下)，但mouseInCanvas是Unity屏幕坐标(Y轴朝上)，两者Y方向相反，
            // 换算时要注意不能直接拿hotspot01.y当屏幕Y偏移用，得先转成"离图片顶边
            // 的距离在Y轴朝上坐标系里怎么表示"。
            Vector2 hotspot01 = _settings.cursorHotspot01;
            Vector2 iconTopLeft = mouseInCanvas - new Vector2(hotspot01.x * size, -hotspot01.y * size);

            RectTransform canvasRt = (RectTransform)_canvas.transform;
            float canvasW = canvasRt.rect.width;
            float canvasH = canvasRt.rect.height;

            // 整张图标的矩形范围卡在屏幕内，不是卡一个点——这是跟系统硬件光标最本质
            // 的区别，图案本身永远不会跑出屏幕外。
            iconTopLeft.x = Mathf.Clamp(iconTopLeft.x, 0f, Mathf.Max(0f, canvasW - size));
            iconTopLeft.y = Mathf.Clamp(iconTopLeft.y, size, canvasH);

            _iconRt.anchoredPosition = new Vector2(iconTopLeft.x, -(canvasH - iconTopLeft.y));
        }

        private void RestoreSystemCursor()
        {
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void EnsureCanvas()
        {
            if (_canvas != null) return;

            var cGo = new GameObject("[SkyPrisonCustomCursorCanvas]");
            DontDestroyOnLoad(cGo);

            _canvas = cGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 排序值给到全项目所有UI最上层，鼠标图标永远盖在所有窗口/HUD上面。
            _canvas.sortingOrder = 32767;

            // 固定像素模式——图标大小不跟着分辨率/UI整体缩放走，跟系统鼠标一个道理。
            var scaler = cGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;

            var iconGo = new GameObject("CursorIcon", typeof(RectTransform));
            iconGo.transform.SetParent(cGo.transform, false);
            _iconRt = iconGo.GetComponent<RectTransform>();
            _iconRt.anchorMin = new Vector2(0f, 1f);
            _iconRt.anchorMax = new Vector2(0f, 1f);
            _iconRt.pivot     = new Vector2(0f, 1f);

            _icon = iconGo.AddComponent<Image>();
            _icon.raycastTarget = false;
            _icon.preserveAspect = true;
        }

        /// <summary>光标自己那个常驻最上层的Canvas——像 PauseMenuController.
        /// HideAllGameCanvases 那种"把所有ScreenSpaceOverlay Canvas一律摁alpha=0藏起来"
        /// 的通用遮盖逻辑，必须显式排除这个Canvas，否则鼠标图标会跟着被藏掉（暂停/
        /// 设置界面看不到鼠标图标就是这个原因）。</summary>
        public static Canvas CursorCanvas => _instance != null ? _instance._canvas : null;

        public static SkyPrisonCustomCursor EnsureInScene()
        {
            if (FindObjectOfType<SkyPrisonCustomCursor>() is { } existing) return existing;
            var go = new GameObject("[SkyPrisonCustomCursor_Runtime]");
            DontDestroyOnLoad(go);
            return go.AddComponent<SkyPrisonCustomCursor>();
        }
    }
}
