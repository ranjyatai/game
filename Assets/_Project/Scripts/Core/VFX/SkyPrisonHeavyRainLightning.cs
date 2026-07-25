using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.VFX
{
    /// <summary>
    /// 暴雨天气专用的打雷闪烁效果——只有 MapWeatherController 判断当前地图是
    /// HeavyRain 时才会挂这个组件（见 MapWeatherController.Setup）。做法：
    /// 用一个全屏白色UI Image做"闪光"（双闪一次，模拟真实闪电常见的两下亮暗），
    /// 闪完之后隔一段随机延迟（模拟声音比光晚到）播一下雷声——雷声音效是可选的，
    /// 项目目前没有现成的雷声素材，没挂就跳过播放，不影响视觉闪烁部分先跑起来。
    /// </summary>
    [DisallowMultipleComponent]
    public class SkyPrisonHeavyRainLightning : MonoBehaviour
    {
        [Header("闪烁节奏")]
        [SerializeField] private float minInterval = 6f;
        [SerializeField] private float maxInterval = 18f;
        [SerializeField] private float flashPeakAlpha = 0.65f;
        [SerializeField] private float flashInDuration = 0.04f;
        [SerializeField] private float flashOutDuration = 0.25f;
        [SerializeField] private float doubleFlashGap = 0.09f;

        [Header("雷声（可选，没有素材就先留空，只播闪光；配了多条就随机挑一条播，" +
                "先闪光后声音模拟光速比声速快的物理现象）")]
        [SerializeField] private AudioClip[] thunderClips;
        [SerializeField] private float thunderMinDelay = 0.4f;
        [SerializeField] private float thunderMaxDelay = 2.2f;
        [SerializeField] private float thunderVolume = 0.8f;

        private Image _flashImage;
        private AudioSource _audioSource;
        private Coroutine _loopRoutine;

        /// <summary>运行时挂到动态生成的物体上，没法在Inspector里预先拖引用，
        /// 靠MapWeatherController生成后调用这个方法把雷声素材灌进来。</summary>
        public void Configure(AudioClip[] clips)
        {
            thunderClips = clips;
        }

        private void Awake()
        {
            BuildFlashOverlay();
            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f; // 雷声是环境音，不需要3D定位
        }

        private void OnEnable()
        {
            _loopRoutine = StartCoroutine(LightningLoop());
        }

        private void OnDisable()
        {
            if (_loopRoutine != null) StopCoroutine(_loopRoutine);
        }

        private void BuildFlashOverlay()
        {
            var canvasGo = new GameObject("LightningFlashCanvas");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 400; // 压在绝大多数UI之上，纯视觉闪光不挡交互（raycastTarget关了）

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight = 0.5f;

            var imgGo = new GameObject("Flash", typeof(RectTransform));
            imgGo.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

            _flashImage = imgGo.AddComponent<Image>();
            _flashImage.color = new Color(1f, 1f, 1f, 0f);
            _flashImage.raycastTarget = false;
        }

        private IEnumerator LightningLoop()
        {
            while (true)
            {
                yield return new WaitForSeconds(Random.Range(minInterval, maxInterval));
                yield return StartCoroutine(DoubleFlash());
                StartCoroutine(PlayThunderAfterDelay());
            }
        }

        // 真实闪电常见"亮一下、暗一瞬、再亮一下"的双闪节奏，比单次淡入淡出更有雷暴感。
        private IEnumerator DoubleFlash()
        {
            yield return Flash();
            yield return new WaitForSeconds(doubleFlashGap);
            yield return Flash(flashPeakAlpha * 0.7f);
        }

        private IEnumerator Flash(float peakAlpha = -1f)
        {
            if (peakAlpha < 0f) peakAlpha = flashPeakAlpha;

            float t = 0f;
            while (t < flashInDuration)
            {
                t += Time.deltaTime;
                SetAlpha(Mathf.Lerp(0f, peakAlpha, t / flashInDuration));
                yield return null;
            }
            SetAlpha(peakAlpha);

            t = 0f;
            while (t < flashOutDuration)
            {
                t += Time.deltaTime;
                SetAlpha(Mathf.Lerp(peakAlpha, 0f, t / flashOutDuration));
                yield return null;
            }
            SetAlpha(0f);
        }

        private void SetAlpha(float a)
        {
            if (_flashImage == null) return;
            var c = _flashImage.color;
            c.a = a;
            _flashImage.color = c;
        }

        private IEnumerator PlayThunderAfterDelay()
        {
            if (thunderClips == null || thunderClips.Length == 0) yield break; // 没有雷声素材，只闪光不出声

            // 闪光已经在调用方(LightningLoop)先播完了，这里再等一段延迟才出声，
            // 模拟"先看到闪电、隔一会儿才听到雷声"的真实物理现象（光速远快于声速）。
            yield return new WaitForSeconds(Random.Range(thunderMinDelay, thunderMaxDelay));
            if (_audioSource == null) yield break;

            AudioClip clip = thunderClips[Random.Range(0, thunderClips.Length)];
            if (clip != null)
                _audioSource.PlayOneShot(clip, thunderVolume);
        }
    }
}
