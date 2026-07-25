using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    public enum SkyPrisonHUDDisplayEnvironmentV1
    {
        FixedWindow = 0,
        Fullscreen = 1,
        SimulatedScreen = 2,
    }

    public enum SkyPrisonHUDResolutionPresetV1
    {
        R1280x720 = 0,
        R1600x900 = 1,
        R1920x1080 = 2,
        R2560x1440 = 3,
        R3840x2160 = 4,
        Custom = 99,
    }

    public enum SkyPrisonHUDAnchorPresetV1
    {
        LeftBottom = 0,
        LeftTop = 1,
        RightBottom = 2,
        RightTop = 3,
        TopCenter = 4,
        BottomCenter = 5,
        RightMiddle = 6,
        LeftMiddle = 7,
        Center = 8,
        Stretch = 9,
    }

    [Serializable]
    public sealed class SkyPrisonHUDModuleLayoutV1
    {
        public SkyPrisonHUDAreaIdV1 areaId = SkyPrisonHUDAreaIdV1.PlayerStatus;
        public bool visible = true;
        public SkyPrisonHUDAnchorPresetV1 anchorPreset = SkyPrisonHUDAnchorPresetV1.LeftBottom;
        public Vector2 offset = new Vector2(30f, 30f);
        public Vector2 size = new Vector2(800f, 130f);
        [Range(0.5f, 2.0f)] public float moduleScale = 1.0f;
        [Range(0f, 1f)] public float alpha = 1.0f;
        public bool lockPositionByInstaller = true;

        public SkyPrisonHUDLayoutPresetV1 ToPreset()
        {
            SkyPrisonHUDLayoutPresetV1 preset = new SkyPrisonHUDLayoutPresetV1();

            switch (anchorPreset)
            {
                case SkyPrisonHUDAnchorPresetV1.LeftBottom:
                    preset.anchorMin = Vector2.zero;
                    preset.anchorMax = Vector2.zero;
                    preset.pivot = Vector2.zero;
                    break;
                case SkyPrisonHUDAnchorPresetV1.LeftTop:
                    preset.anchorMin = new Vector2(0f, 1f);
                    preset.anchorMax = new Vector2(0f, 1f);
                    preset.pivot = new Vector2(0f, 1f);
                    break;
                case SkyPrisonHUDAnchorPresetV1.RightBottom:
                    preset.anchorMin = new Vector2(1f, 0f);
                    preset.anchorMax = new Vector2(1f, 0f);
                    preset.pivot = new Vector2(1f, 0f);
                    break;
                case SkyPrisonHUDAnchorPresetV1.RightTop:
                    preset.anchorMin = Vector2.one;
                    preset.anchorMax = Vector2.one;
                    preset.pivot = Vector2.one;
                    break;
                case SkyPrisonHUDAnchorPresetV1.TopCenter:
                    preset.anchorMin = new Vector2(0.5f, 1f);
                    preset.anchorMax = new Vector2(0.5f, 1f);
                    preset.pivot = new Vector2(0.5f, 1f);
                    break;
                case SkyPrisonHUDAnchorPresetV1.BottomCenter:
                    preset.anchorMin = new Vector2(0.5f, 0f);
                    preset.anchorMax = new Vector2(0.5f, 0f);
                    preset.pivot = new Vector2(0.5f, 0f);
                    break;
                case SkyPrisonHUDAnchorPresetV1.RightMiddle:
                    preset.anchorMin = new Vector2(1f, 0.5f);
                    preset.anchorMax = new Vector2(1f, 0.5f);
                    preset.pivot = new Vector2(1f, 0.5f);
                    break;
                case SkyPrisonHUDAnchorPresetV1.LeftMiddle:
                    preset.anchorMin = new Vector2(0f, 0.5f);
                    preset.anchorMax = new Vector2(0f, 0.5f);
                    preset.pivot = new Vector2(0f, 0.5f);
                    break;
                case SkyPrisonHUDAnchorPresetV1.Center:
                    preset.anchorMin = new Vector2(0.5f, 0.5f);
                    preset.anchorMax = new Vector2(0.5f, 0.5f);
                    preset.pivot = new Vector2(0.5f, 0.5f);
                    break;
                case SkyPrisonHUDAnchorPresetV1.Stretch:
                    preset.anchorMin = Vector2.zero;
                    preset.anchorMax = Vector2.one;
                    preset.pivot = new Vector2(0.5f, 0.5f);
                    preset.anchoredPosition = Vector2.zero;
                    preset.sizeDelta = Vector2.zero;
                    return preset;
            }

            preset.anchoredPosition = offset;
            preset.sizeDelta = size;
            return preset;
        }
    }

    [Serializable]
    public sealed class SkyPrisonPlayerHUDVisualLayoutV1
    {
        [Header("Root")]
        public Vector2 rootSize = new Vector2(800f, 130f);

        [Header("Legacy Quick Slots - moved to context.quickSlots in V32")]
        [HideInInspector] public Vector2 quickSlotRootPosition = new Vector2(48f, -16f);
        [HideInInspector] public Vector2 quickSlotRootSize = new Vector2(520f, 40f);
        [HideInInspector] public Vector2 quickSlotSize = new Vector2(118f, 34f);
        [HideInInspector] public float quickSlotGap = 14f;
        [HideInInspector] public float quickSlotIconSize = 30f;

        [Header("Bars")]
        public Vector2 hpRootPosition = new Vector2(22f, 39f);
        public Vector2 lpRootPosition = new Vector2(22f, 17f);

        [Header("HP / LP Labels")]
        public Vector2 hpLabelPosition = Vector2.zero;
        public Vector2 lpLabelPosition = Vector2.zero;
        public Vector2 hpLabelSize = new Vector2(32f, 18f);
        public Vector2 lpLabelSize = new Vector2(32f, 18f);
        public int hpLabelFontSize = 13;
        public int lpLabelFontSize = 13;
        public float hpLabelCharacterSpacing = 0f;
        public float lpLabelCharacterSpacing = 0f;

        [Header("Bars")]
        public float labelWidth = 32f;
        public float barX = 40f;
        public float hpBarWidth = 600f;
        public float lpBarWidth = 600f;
        public float hpBarHeight = 7f;
        public float lpBarHeight = 6f;

        [Header("Load")]
        public Vector2 loadTextPosition = new Vector2(665f, 14f);
        public Vector2 loadTextSize = new Vector2(300f, 28f);
        public int loadFontSize = 18;
        public float loadCharacterSpacing = 9f;
        public float loadPercentOffsetX = 0f;
        public float loadStatusOffsetX = 0f;

        [Header("Text")]
        public int labelFontSize = 13;

        [HideInInspector] public int slotNumberFontSize = 9;
    }

    [Serializable]
    public sealed class SkyPrisonQuickSlotsHUDVisualLayoutV1
    {
        [Header("Root")]
        public Vector2 rootSize = new Vector2(520f, 40f);

        [Header("Slots")]
        public Vector2 slotSize = new Vector2(118f, 34f);
        public float slotGap = 14f;
        public float iconSize = 30f;
        public int slotNumberFontSize = 9;
    }

    [Serializable]
    public sealed class SkyPrisonHUDLayoutContextV1
    {
        public string displayName = "FixedWindow 1920x1080";
        public SkyPrisonHUDDisplayEnvironmentV1 environment = SkyPrisonHUDDisplayEnvironmentV1.FixedWindow;
        public SkyPrisonHUDResolutionPresetV1 resolutionPreset = SkyPrisonHUDResolutionPresetV1.R1920x1080;
        public Vector2Int customResolution = new Vector2Int(1920, 1080);
        [Range(0.5f, 2.0f)] public float globalHudScale = 0.90f;
        public Vector2 safeMargin = new Vector2(30f, 30f);
        public List<SkyPrisonHUDModuleLayoutV1> modules = new List<SkyPrisonHUDModuleLayoutV1>();
        public SkyPrisonPlayerHUDVisualLayoutV1 playerHud = new SkyPrisonPlayerHUDVisualLayoutV1();
        public SkyPrisonQuickSlotsHUDVisualLayoutV1 quickSlots = new SkyPrisonQuickSlotsHUDVisualLayoutV1();

        // V34: one-shot migration guard.
        // Without this, EnsureDefaults() copies legacy PlayerStatus quick slot values back
        // into the independent QuickSlots layout every OnGUI/OnValidate pass.
        [HideInInspector] public bool quickSlotsMigrationV32Done = false;

        public SkyPrisonHUDModuleLayoutV1 GetOrCreateModule(SkyPrisonHUDAreaIdV1 areaId)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] != null && modules[i].areaId == areaId)
                    return modules[i];
            }

            SkyPrisonHUDModuleLayoutV1 module = SkyPrisonHUDLayoutProfile_V1.CreateDefaultModule(areaId);
            modules.Add(module);
            return module;
        }
    }

    [CreateAssetMenu(fileName = "SkyPrisonHUDLayoutProfile_V1", menuName = "Sky Prison/UI/HUD Layout Profile V1", order = 2200)]
    public sealed class SkyPrisonHUDLayoutProfile_V1 : ScriptableObject
    {
        public const int DesignWidth = 1920;
        public const int DesignHeight = 1080;

        public SkyPrisonHUDDisplayEnvironmentV1 defaultEnvironment = SkyPrisonHUDDisplayEnvironmentV1.FixedWindow;
        public SkyPrisonHUDResolutionPresetV1 defaultResolution = SkyPrisonHUDResolutionPresetV1.R1920x1080;
        public List<SkyPrisonHUDLayoutContextV1> contexts = new List<SkyPrisonHUDLayoutContextV1>();

        private void OnValidate()
        {
            EnsureDefaults();
        }

        public void EnsureDefaults()
        {
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.FixedWindow, SkyPrisonHUDResolutionPresetV1.R1280x720, 0.78f, new Vector2Int(1280, 720));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.FixedWindow, SkyPrisonHUDResolutionPresetV1.R1600x900, 0.84f, new Vector2Int(1600, 900));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.FixedWindow, SkyPrisonHUDResolutionPresetV1.R1920x1080, 0.88f, new Vector2Int(1920, 1080));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.FixedWindow, SkyPrisonHUDResolutionPresetV1.R2560x1440, 1.00f, new Vector2Int(2560, 1440));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.FixedWindow, SkyPrisonHUDResolutionPresetV1.R3840x2160, 1.00f, new Vector2Int(3840, 2160));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.Fullscreen, SkyPrisonHUDResolutionPresetV1.R1920x1080, 0.88f, new Vector2Int(1920, 1080));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.Fullscreen, SkyPrisonHUDResolutionPresetV1.R2560x1440, 1.00f, new Vector2Int(2560, 1440));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.Fullscreen, SkyPrisonHUDResolutionPresetV1.R3840x2160, 1.00f, new Vector2Int(3840, 2160));
            EnsureContext(SkyPrisonHUDDisplayEnvironmentV1.SimulatedScreen, SkyPrisonHUDResolutionPresetV1.R1920x1080, 1.00f, new Vector2Int(1920, 1080));

            for (int i = 0; i < contexts.Count; i++)
            {
                SkyPrisonHUDLayoutContextV1 context = contexts[i];
                if (context == null)
                    continue;

                EnsureAllModules(context);
                EnsureVisualLayoutDefaults(context);
            }
        }

        public SkyPrisonHUDLayoutContextV1 GetContext(SkyPrisonHUDDisplayEnvironmentV1 environment, SkyPrisonHUDResolutionPresetV1 resolutionPreset)
        {
            EnsureDefaults();
            for (int i = 0; i < contexts.Count; i++)
            {
                SkyPrisonHUDLayoutContextV1 context = contexts[i];
                if (context != null && context.environment == environment && context.resolutionPreset == resolutionPreset)
                    return context;
            }

            return EnsureContext(environment, resolutionPreset, 0.9f, GetResolution(resolutionPreset));
        }

        public SkyPrisonHUDLayoutContextV1 GetDefaultContext()
        {
            return GetContext(defaultEnvironment, defaultResolution);
        }

        public static Vector2Int GetResolution(SkyPrisonHUDResolutionPresetV1 preset)
        {
            switch (preset)
            {
                case SkyPrisonHUDResolutionPresetV1.R1280x720: return new Vector2Int(1280, 720);
                case SkyPrisonHUDResolutionPresetV1.R1600x900: return new Vector2Int(1600, 900);
                case SkyPrisonHUDResolutionPresetV1.R1920x1080: return new Vector2Int(1920, 1080);
                case SkyPrisonHUDResolutionPresetV1.R2560x1440: return new Vector2Int(2560, 1440);
                case SkyPrisonHUDResolutionPresetV1.R3840x2160: return new Vector2Int(3840, 2160);
                default: return new Vector2Int(1920, 1080);
            }
        }

        private SkyPrisonHUDLayoutContextV1 EnsureContext(SkyPrisonHUDDisplayEnvironmentV1 environment, SkyPrisonHUDResolutionPresetV1 preset, float scale, Vector2Int resolution)
        {
            for (int i = 0; i < contexts.Count; i++)
            {
                SkyPrisonHUDLayoutContextV1 existing = contexts[i];
                if (existing != null && existing.environment == environment && existing.resolutionPreset == preset)
                {
                    EnsureAllModules(existing);
                    EnsureVisualLayoutDefaults(existing);
                    return existing;
                }
            }

            SkyPrisonHUDLayoutContextV1 context = new SkyPrisonHUDLayoutContextV1
            {
                environment = environment,
                resolutionPreset = preset,
                customResolution = resolution,
                globalHudScale = scale,
                safeMargin = new Vector2(30f, 30f),
                displayName = environment + " " + resolution.x + "x" + resolution.y,
            };
            EnsureAllModules(context);
            EnsureVisualLayoutDefaults(context);
            contexts.Add(context);
            return context;
        }

        private static void EnsureVisualLayoutDefaults(SkyPrisonHUDLayoutContextV1 context)
        {
            if (context == null)
                return;

            if (context.playerHud == null)
                context.playerHud = new SkyPrisonPlayerHUDVisualLayoutV1();

            if (context.quickSlots == null)
                context.quickSlots = new SkyPrisonQuickSlotsHUDVisualLayoutV1();

            // V34 fix:
            // V32 moved QuickSlots out of PlayerStatus, but its migration still ran every
            // EnsureDefaults() call. The HUD workbench calls EnsureDefaults() from OnGUI,
            // so any typed QuickSlots value was immediately overwritten by hidden legacy
            // PlayerStatus quickSlot* fields on the next repaint.
            // Migration must be one-shot only. After this flag is true, QuickSlots owns
            // its own values and PlayerStatus legacy fields are no longer allowed to write back.
            if (!context.quickSlotsMigrationV32Done)
            {
                if (context.playerHud.quickSlotRootSize.x > 0.01f && context.playerHud.quickSlotRootSize.y > 0.01f)
                    context.quickSlots.rootSize = context.playerHud.quickSlotRootSize;

                if (context.playerHud.quickSlotSize.x > 0.01f && context.playerHud.quickSlotSize.y > 0.01f)
                    context.quickSlots.slotSize = context.playerHud.quickSlotSize;

                if (context.playerHud.quickSlotGap > 0.01f)
                    context.quickSlots.slotGap = context.playerHud.quickSlotGap;

                if (context.playerHud.quickSlotIconSize > 0.01f)
                    context.quickSlots.iconSize = context.playerHud.quickSlotIconSize;

                if (context.playerHud.slotNumberFontSize > 0)
                    context.quickSlots.slotNumberFontSize = context.playerHud.slotNumberFontSize;

                context.quickSlotsMigrationV32Done = true;
            }
        }

        private static void EnsureAllModules(SkyPrisonHUDLayoutContextV1 context)
        {
            if (context == null)
                return;

            // 迁移：PlayerStatus LeftBottom(30,30)→LeftTop(30,-30)→BottomCenter(0,80)
            // 迁移：QuickSlots LeftBottom(78,104)→BottomCenter(0,30)
            foreach (var m in context.modules)
            {
                if (m == null) continue;
                if (m.areaId == SkyPrisonHUDAreaIdV1.PlayerStatus
                    && m.anchorPreset != SkyPrisonHUDAnchorPresetV1.BottomCenter)
                {
                    m.anchorPreset = SkyPrisonHUDAnchorPresetV1.BottomCenter;
                    m.offset = new Vector2(0f, 160f);
                }
                if (m.areaId == SkyPrisonHUDAreaIdV1.QuickSlots
                    && m.anchorPreset != SkyPrisonHUDAnchorPresetV1.BottomCenter)
                {
                    m.anchorPreset = SkyPrisonHUDAnchorPresetV1.BottomCenter;
                    m.offset = new Vector2(0f, 30f);
                }
                // 迁移：TargetInfo 旧默认 visible=true → false
                if (m.areaId == SkyPrisonHUDAreaIdV1.TargetInfo && m.visible)
                    m.visible = false;
            }

            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.PlayerStatus);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.QuickSlots);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.TargetInfo);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.BossInfo);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.MissionInfo);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.InteractionPrompt);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.DamageNumbers);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.StatusNotifications);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.Dialogue);
            context.GetOrCreateModule(SkyPrisonHUDAreaIdV1.SystemOverlay);
        }

        public static SkyPrisonHUDModuleLayoutV1 CreateDefaultModule(SkyPrisonHUDAreaIdV1 areaId)
        {
            SkyPrisonHUDModuleLayoutV1 module = new SkyPrisonHUDModuleLayoutV1 { areaId = areaId };
            switch (areaId)
            {
                case SkyPrisonHUDAreaIdV1.PlayerStatus:
                    module.visible = true;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.BottomCenter;
                    module.offset = new Vector2(0f, 160f);
                    module.size = new Vector2(800f, 130f);
                    break;
                case SkyPrisonHUDAreaIdV1.QuickSlots:
                    module.visible = true;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.BottomCenter;
                    module.offset = new Vector2(0f, 30f);
                    module.size = new Vector2(520f, 40f);
                    break;
                case SkyPrisonHUDAreaIdV1.TargetInfo:
                    module.visible = false;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.TopCenter;
                    module.offset = new Vector2(0f, -36f);
                    module.size = new Vector2(680f, 86f);
                    break;
                case SkyPrisonHUDAreaIdV1.BossInfo:
                    module.visible = false;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.TopCenter;
                    module.offset = new Vector2(0f, -86f);
                    module.size = new Vector2(900f, 72f);
                    break;
                case SkyPrisonHUDAreaIdV1.MissionInfo:
                    module.visible = true;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.RightMiddle;
                    module.offset = new Vector2(-40f, 0f);
                    module.size = new Vector2(420f, 360f);
                    break;
                case SkyPrisonHUDAreaIdV1.InteractionPrompt:
                    module.visible = true;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.BottomCenter;
                    module.offset = new Vector2(0f, 120f);
                    module.size = new Vector2(520f, 80f);
                    break;
                case SkyPrisonHUDAreaIdV1.DamageNumbers:
                case SkyPrisonHUDAreaIdV1.SystemOverlay:
                    module.visible = true;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.Stretch;
                    module.offset = Vector2.zero;
                    module.size = Vector2.zero;
                    break;
                case SkyPrisonHUDAreaIdV1.StatusNotifications:
                    module.visible = true;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.RightMiddle;
                    module.offset = new Vector2(-40f, -240f);
                    module.size = new Vector2(420f, 220f);
                    break;
                case SkyPrisonHUDAreaIdV1.Dialogue:
                    module.visible = false;
                    module.anchorPreset = SkyPrisonHUDAnchorPresetV1.BottomCenter;
                    module.offset = new Vector2(0f, 48f);
                    module.size = new Vector2(1280f, 240f);
                    break;
            }

            return module;
        }
    }
}
