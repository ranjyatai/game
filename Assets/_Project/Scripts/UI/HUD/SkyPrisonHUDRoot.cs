using System.Collections.Generic;
using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class SkyPrisonHUDRoot_V1 : MonoBehaviour
    {
        [Header("Global Visibility")]
        [SerializeField] private bool hudVisible = true;

        [Header("HUD Scale")]
        [SerializeField, Range(0.5f, 2.0f)] private float hudScale = 1.0f;

        [Header("Current Mode")]
        [SerializeField] private SkyPrisonHUDDisplayModeV1 currentMode = SkyPrisonHUDDisplayModeV1.Combat;

        private readonly Dictionary<SkyPrisonHUDAreaIdV1, SkyPrisonHUDArea_V1> areaMap = new Dictionary<SkyPrisonHUDAreaIdV1, SkyPrisonHUDArea_V1>();
        private CanvasGroup rootCanvasGroup;

        public bool HudVisible => hudVisible;
        public float HudScale => hudScale;
        public SkyPrisonHUDDisplayModeV1 CurrentMode => currentMode;

        public event System.Action<bool> OnHudVisibilityChanged;

        private void Awake()
        {
            RebuildAreaCache();
            EnsureRootCanvasGroup();
            ApplyAll();
        }

        private void OnValidate()
        {
            hudScale = Mathf.Clamp(hudScale, 0.5f, 2.0f);
        }

        public void RebuildAreaCache()
        {
            areaMap.Clear();
            SkyPrisonHUDArea_V1[] areas = GetComponentsInChildren<SkyPrisonHUDArea_V1>(true);
            for (int i = 0; i < areas.Length; i++)
            {
                SkyPrisonHUDArea_V1 area = areas[i];
                if (area == null)
                    continue;

                areaMap[area.AreaId] = area;
            }
        }

        public void SetHUDVisible(bool visible)
        {
            hudVisible = visible;
            EnsureRootCanvasGroup();
            rootCanvasGroup.alpha = visible ? 1f : 0f;
            rootCanvasGroup.interactable = visible;
            rootCanvasGroup.blocksRaycasts = visible;
            OnHudVisibilityChanged?.Invoke(visible);
        }

        public void SetAreaVisible(SkyPrisonHUDAreaIdV1 areaId, bool visible)
        {
            if (areaMap.Count == 0)
                RebuildAreaCache();

            if (areaMap.TryGetValue(areaId, out SkyPrisonHUDArea_V1 area) && area != null)
                area.SetVisible(visible, true);
        }

        public void SetHUDScale(float scale)
        {
            hudScale = Mathf.Clamp(scale, 0.5f, 2.0f);

            if (areaMap.Count == 0)
                RebuildAreaCache();

            foreach (SkyPrisonHUDArea_V1 area in areaMap.Values)
            {
                if (area != null)
                    area.SetScale(hudScale);
            }
        }

        public void SetDisplayMode(SkyPrisonHUDDisplayModeV1 mode)
        {
            currentMode = mode;

            switch (mode)
            {
                case SkyPrisonHUDDisplayModeV1.Combat:
                    SetHUDVisible(true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.PlayerStatus, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.QuickSlots, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.TargetInfo, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.MissionInfo, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.InteractionPrompt, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.DamageNumbers, true);
                    break;

                case SkyPrisonHUDDisplayModeV1.Exploration:
                    SetHUDVisible(true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.PlayerStatus, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.QuickSlots, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.TargetInfo, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.MissionInfo, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.InteractionPrompt, true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.DamageNumbers, false);
                    break;

                case SkyPrisonHUDDisplayModeV1.Dialogue:
                    SetHUDVisible(true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.PlayerStatus, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.QuickSlots, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.TargetInfo, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.MissionInfo, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.InteractionPrompt, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.DamageNumbers, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.Dialogue, true);
                    break;

                case SkyPrisonHUDDisplayModeV1.Terminal:
                case SkyPrisonHUDDisplayModeV1.Scan:
                    SetHUDVisible(true);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.PlayerStatus, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.QuickSlots, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.TargetInfo, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.MissionInfo, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.InteractionPrompt, false);
                    SetAreaVisible(SkyPrisonHUDAreaIdV1.SystemOverlay, true);
                    break;

                case SkyPrisonHUDDisplayModeV1.Cutscene:
                case SkyPrisonHUDDisplayModeV1.Hidden:
                    SetHUDVisible(false);
                    break;
            }
        }

        public SkyPrisonHUDArea_V1 GetArea(SkyPrisonHUDAreaIdV1 areaId)
        {
            if (areaMap.Count == 0)
                RebuildAreaCache();

            areaMap.TryGetValue(areaId, out SkyPrisonHUDArea_V1 area);
            return area;
        }

        private void ApplyAll()
        {
            SetHUDVisible(hudVisible);
            SetHUDScale(hudScale);
            SetDisplayMode(currentMode);
        }

        private void EnsureRootCanvasGroup()
        {
            if (rootCanvasGroup == null)
                rootCanvasGroup = GetComponent<CanvasGroup>();

            if (rootCanvasGroup == null)
                rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }
}
