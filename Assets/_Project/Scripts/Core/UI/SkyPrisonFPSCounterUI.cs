using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 左上角 FPS 计数器，由设置里的"显示FPS"开关控制。跟游戏内其它窗口无关，
/// 常驻 DontDestroyOnLoad，跨场景保留，开关状态由 SaveManager.Settings.showFps 驱动
/// （游戏启动时 SaveManager.Awake 读一次存档设置来同步初始显示状态）。
/// </summary>
public class SkyPrisonFPSCounterUI : MonoBehaviour
{
    private static SkyPrisonFPSCounterUI _instance;

    private TMP_Text _text;
    private float _accumTime;
    private int _accumFrames;
    private float _fps;

    public static void SetVisible(bool visible)
    {
        if (visible)
        {
            if (_instance == null) Create();
            _instance.gameObject.SetActive(true);
        }
        else
        {
            if (_instance != null) _instance.gameObject.SetActive(false);
        }
    }

    private static void Create()
    {
        var go = new GameObject("[FPSCounter]");
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<SkyPrisonFPSCounterUI>();
        _instance.Build();
    }

    private void Build()
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32700; // 压过所有窗口，常驻可见
        canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(3840f, 2160f);

        var rt = canvasGo.GetComponent<RectTransform>();
        var textRt = new GameObject("FPSText").AddComponent<RectTransform>();
        textRt.SetParent(rt, false);
        textRt.anchorMin = textRt.anchorMax = new Vector2(0f, 1f);
        textRt.pivot = new Vector2(0f, 1f);
        textRt.sizeDelta = new Vector2(320f, 64f);
        textRt.anchoredPosition = new Vector2(24f, -24f);

        _text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
        _text.text = "FPS --";
        _text.fontSize = 36f;
        _text.color = new Color(0.42f, 0.92f, 0.68f, 0.9f);
        _text.alignment = TextAlignmentOptions.TopLeft;
        _text.raycastTarget = false;
    }

    private void Update()
    {
        _accumTime += Time.unscaledDeltaTime;
        _accumFrames++;
        if (_accumTime >= 0.25f)
        {
            _fps = _accumFrames / _accumTime;
            _accumTime = 0f;
            _accumFrames = 0;
            if (_text != null) _text.text = $"FPS {Mathf.RoundToInt(_fps)}";
        }
    }
}
