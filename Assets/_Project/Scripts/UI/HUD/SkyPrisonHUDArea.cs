using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SkyPrisonHUDArea_V1 : MonoBehaviour
    {
        [SerializeField] private SkyPrisonHUDAreaIdV1 areaId = SkyPrisonHUDAreaIdV1.PlayerStatus;
        [SerializeField] private bool defaultVisible = true;
        [SerializeField] private bool lockPositionByInstaller = true;
        [SerializeField, Range(0.01f, 4.0f)] private float moduleScale = 1.0f;

        private CanvasGroup canvasGroup;
        private RectTransform cachedRectTransform;

        public SkyPrisonHUDAreaIdV1 AreaId => areaId;
        public bool DefaultVisible => defaultVisible;
        public bool LockPositionByInstaller => lockPositionByInstaller;
        public float ModuleScale => moduleScale;
        public RectTransform RectTransform => cachedRectTransform != null ? cachedRectTransform : (cachedRectTransform = GetComponent<RectTransform>());

        private void Awake()
        {
            EnsureCanvasGroup();
            SetVisible(defaultVisible, true);
        }

        private void Reset()
        {
            EnsureCanvasGroup();
        }

        public void Configure(SkyPrisonHUDAreaIdV1 id, bool visibleByDefault, bool lockPosition)
        {
            Configure(id, visibleByDefault, lockPosition, moduleScale);
        }

        public void Configure(SkyPrisonHUDAreaIdV1 id, bool visibleByDefault, bool lockPosition, float scaleByModule)
        {
            areaId = id;
            defaultVisible = visibleByDefault;
            lockPositionByInstaller = lockPosition;
            moduleScale = Mathf.Max(0.01f, scaleByModule);
            SetVisible(defaultVisible, true);
        }

        public void SetVisible(bool visible, bool instant = true)
        {
            EnsureCanvasGroup();
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;

            if (instant)
                gameObject.SetActive(visible);
        }

        public void SetScale(float scale)
        {
            // V35: keep installer-authored moduleScale.
            // HUDRoot.SetHUDScale() is global-only and used to overwrite per-module scale,
            // which made installed scene coordinates/sizes differ from the workbench preview.
            RectTransform.localScale = Vector3.one * Mathf.Max(0.01f, scale) * Mathf.Max(0.01f, moduleScale);
        }

        private void EnsureCanvasGroup()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();

            if (cachedRectTransform == null)
                cachedRectTransform = GetComponent<RectTransform>();
        }
    }
}
