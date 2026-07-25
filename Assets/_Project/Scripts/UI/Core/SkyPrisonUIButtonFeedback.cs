using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 通用 UI 按钮交互反馈：悬停半透明冷绿，点击冷绿亮闪后渐隐恢复（近似"激活发光"）。
    /// 默认自动收集按钮下所有「可见」图形（边框线 + 图标/文字），整块（连框）一起变绿；
    /// 透明背景（alpha≈0）会被跳过，不会被填成绿色。按钮自身点击逻辑不受影响。
    ///
    /// 全局用法：在按钮生成处 SkyPrisonUIButtonFeedback.Attach(buttonGO) 即可（幂等，不会重复挂）。
    /// 需要只染指定图形时用 Configure(params Graphic[])。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonUIButtonFeedback : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        [SerializeField] private Color hoverColor = new Color(0.42f, 0.92f, 0.68f, 0.55f); // 半透明冷绿
        [SerializeField] private Color flashColor = new Color(0.62f, 1.00f, 0.82f, 1f);    // 点击瞬间亮冷绿
        [SerializeField] private float flashDuration = 0.35f;
        [Tooltip("低于该 alpha 的图形视为透明背景，跳过不染（避免把透明底填成绿）。")]
        [SerializeField] private float minAlphaToTint = 0.04f;
        [Tooltip("关掉之后鼠标悬停不再整块染绿，但点击闪光反馈照常——商店价格条这种" +
                 "\"不要hover变绿、但要有点击激活感\"的场景用这个。")]
        [SerializeField] private bool enableHoverTint = true;
        [Tooltip("关掉之后点击不再由这个组件自己播确认音效——调用方自己已经按结果" +
                 "(成功/库存不足)决定播哪个音效的场景用这个，不然会跟调用方的音效双重播放。")]
        [SerializeField] private bool playClickSE = true;

        // 之前是纯运行时字段，不会被序列化——编辑器生成脚本里在烘焙 prefab 时调用
        // Configure()排除某些子图形(比如结账按钮右上角的购物车角标)，这个排除结果
        // 从来没有真正生效过：Start()在运行时重新执行时 _initialized 已经因为没被
        // 序列化又变回 false，直接把 Configure() 的结果整个覆盖掉，又变回自动收集
        // 全部子图形——角标"hover时还是跟着变半透明"就是这个原因。改成序列化
        // _targets，Start() 优先复用编辑器时烘焙进去的显式列表。
        [SerializeField] private Graphic[] _targets;
        private Color[] _normals;
        private bool _initialized;
        private bool _hovering;
        private float _flash; // 1→0 衰减

        private void Start()
        {
            if (_initialized) return;
            if (_targets != null && _targets.Length > 0) SetTargets(_targets);
            else AutoCollect();
        }

        /// <summary>幂等挂载：按钮上没有就加，已有则返回现成的；随后自动收集图形。</summary>
        public static SkyPrisonUIButtonFeedback Attach(GameObject button)
        {
            if (button == null) return null;
            var fb = button.GetComponent<SkyPrisonUIButtonFeedback>();
            if (fb == null) fb = button.AddComponent<SkyPrisonUIButtonFeedback>();
            return fb;
        }

        /// <summary>显式指定要染的图形（覆盖自动收集）。</summary>
        public void Configure(params Graphic[] graphics)
        {
            SetTargets(graphics);
        }

        private void AutoCollect()
        {
            Graphic[] all = GetComponentsInChildren<Graphic>(true);
            var list = new List<Graphic>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                Graphic g = all[i];
                if (g != null && g.color.a > minAlphaToTint)
                    list.Add(g);
            }
            SetTargets(list.ToArray());
        }

        private void SetTargets(Graphic[] graphics)
        {
            _targets = graphics ?? new Graphic[0];
            _normals = new Color[_targets.Length];
            for (int i = 0; i < _targets.Length; i++)
                _normals[i] = _targets[i] != null ? _targets[i].color : Color.white;
            _initialized = true;
        }

        public void OnPointerEnter(PointerEventData e)
        {
            _hovering = true;
            if (_flash <= 0f) ApplyBase();
        }

        public void OnPointerExit(PointerEventData e)
        {
            _hovering = false;
            if (_flash <= 0f) ApplyBase();
        }

        public void OnPointerClick(PointerEventData e)
        {
            _flash = 1f;
            if (playClickSE) SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.Confirm);
        }

        private void Update()
        {
            if (_flash <= 0f) return;

            _flash -= Time.unscaledDeltaTime / Mathf.Max(0.01f, flashDuration);
            if (_flash < 0f) _flash = 0f;

            if (_targets == null) return;
            for (int i = 0; i < _targets.Length; i++)
            {
                if (_targets[i] == null) continue;
                Color baseColor = (_hovering && enableHoverTint) ? hoverColor : _normals[i];
                _targets[i].color = Color.Lerp(baseColor, flashColor, _flash);
            }
        }

        // 非闪光态下，按 hover 与否把所有图形设到 hover 绿或各自原色。
        private void ApplyBase()
        {
            if (_targets == null) return;
            for (int i = 0; i < _targets.Length; i++)
            {
                if (_targets[i] == null) continue;
                _targets[i].color = (_hovering && enableHoverTint) ? hoverColor : _normals[i];
            }
        }
    }
}
