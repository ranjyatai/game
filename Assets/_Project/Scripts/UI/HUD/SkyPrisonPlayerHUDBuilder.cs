using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Single source of truth for building the player status HUD module. V32 keeps QuickSlots as an independent HUD area.
    /// Editor installer, prefab generation and future preview tools should call this builder instead of duplicating layout code.
    /// V88: HUD Chromatic now uses direct material application on actual Image/RawImage/TMP nodes.
    /// The deprecated duplicated Red/Cyan overlay route must not be used.
    /// </summary>
    public static class SkyPrisonPlayerHUDBuilder
    {
        // V87: Workbench can suppress module-RT wrapping while drawing its editor preview.
        // Scene installation keeps this false, so the real Scene HUD still uses module RT postprocess.
        public static bool SuppressModulePostProcessForWorkbenchPreview = false;
        public static GameObject BuildPlayerHUDObject(SkyPrisonHUDLayoutContextV1 context, SkyPrisonHUDStyleProfile_V1 style)
        {
            SkyPrisonPlayerHUDVisualLayoutV1 layout = context.playerHud;
            GameObject root = CreateUIObject("PF_UIModule_PlayerStatus_LeftBottom_V32_NoQuickSlots", null);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = layout.rootSize;

            // V16 visible default template rule:
            // The HUD prefab must be visible before final art sprites are assigned.
            // Missing sprites are drawn as solid-color prototype Images.
            Image glass = CreateImage("BasePlate", root.transform, style.basePlateColor, style.basePlateSprite, style.glassPanelMaterial != null ? style.glassPanelMaterial : style.defaultUIMaterial, style.basePlateImageType);
            SetRect(glass.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            // V32: PlayerStatus must not build QuickSlot UI inside itself.
            // Quick slots are an independent UI module/area and will be built by its own module builder later.
            Image[] slotIcons = System.Array.Empty<Image>();

            Transform hpRoot = CreateUIObject("HpBarRoot", root.transform).transform;
            SetRect(hpRoot.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, layout.hpRootPosition, new Vector2(layout.barX + layout.hpBarWidth + 20f, 22f));
            Vector2 hpLabelSize = layout.hpLabelSize.x > 0.01f && layout.hpLabelSize.y > 0.01f ? layout.hpLabelSize : new Vector2(layout.labelWidth, 18f);
            TMP_Text hpLabel = CreateText("HpLabel", hpRoot, style.ResolveHpLabelText(), Mathf.RoundToInt(layout.hpLabelFontSize * style.fontScale), TextAlignmentOptions.Left, style.playerHpLabelColor, style.ResolveHpLabelFont());
            hpLabel.characterSpacing = layout.hpLabelCharacterSpacing;
            SetRect(hpLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), layout.hpLabelPosition, hpLabelSize);
            ApplyTextAspect(hpLabel, style.ResolveHpLabelAspect());
            RectTransform hpBack = CreateImageRect("HpBack", hpRoot, style.barBackColor, style.hpBarBackSprite, style.defaultUIMaterial, style.barFillImageType, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(layout.barX, 0f), new Vector2(layout.hpBarWidth, layout.hpBarHeight)).rectTransform;

            // HP uses fixed Fill images clipped by RectMask2D roots.
            // This avoids compressing the actual fill sprite when HP changes.
            RectTransform hpDelayMask = CreateMaskedFill("HpDamageLagMaskRoot", "HpDamageLagFill", hpRoot, style.hpDamageLagColor, style.hpDamageLagFillSprite, style.hpDamageLagMaterial, style.barFillImageType, new Vector2(layout.barX, 0f), new Vector2(layout.hpBarWidth, layout.hpBarHeight));
            RectTransform hpFillMask = CreateMaskedFill("HpFillMaskRoot", "HpFill", hpRoot, style.hpColor, style.hpBarFillSprite, style.hpFillMaterial, style.barFillImageType, new Vector2(layout.barX, 0f), new Vector2(layout.hpBarWidth, layout.hpBarHeight));
            // V52: Current HP must render above the damage-lag layer.
            // Red loss feedback is only visible where the current green fill no longer covers it.
            hpDelayMask.SetAsLastSibling();
            hpFillMask.SetAsLastSibling();

            if (style.hpBarFrameSprite != null)
            {
                Image frame = CreateImage("HpFrame", hpRoot, style.barFrameColor, style.hpBarFrameSprite, style.barFrameMaterial, style.barFrameImageType);
                SetRect(frame.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(layout.barX, 0f), new Vector2(layout.hpBarWidth, layout.hpBarHeight + 8f));
            }
            hpBack.SetAsFirstSibling();

            Transform lpRoot = CreateUIObject("LpBarRoot", root.transform).transform;
            SetRect(lpRoot.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, layout.lpRootPosition, new Vector2(layout.barX + layout.lpBarWidth + 20f, 22f));
            Vector2 lpLabelSize = layout.lpLabelSize.x > 0.01f && layout.lpLabelSize.y > 0.01f ? layout.lpLabelSize : new Vector2(layout.labelWidth, 18f);
            TMP_Text lpLabel = CreateText("LpLabel", lpRoot, style.ResolveLpLabelText(), Mathf.RoundToInt(layout.lpLabelFontSize * style.fontScale), TextAlignmentOptions.Left, style.playerLpLabelColor, style.ResolveLpLabelFont());
            lpLabel.characterSpacing = layout.lpLabelCharacterSpacing;
            SetRect(lpLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), layout.lpLabelPosition, lpLabelSize);
            ApplyTextAspect(lpLabel, style.ResolveLpLabelAspect());
            RectTransform lpBack = CreateImageRect("LpBack", lpRoot, style.barBackColor, style.lpBarBackSprite, style.defaultUIMaterial, style.barFillImageType, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(layout.barX, 0f), new Vector2(layout.lpBarWidth, layout.lpBarHeight)).rectTransform;

            // LP also uses masked clipping. Ghost layer is generated but disabled by default.
            RectTransform lpGhostMask = CreateMaskedFill("LpGhostMaskRoot", "LpGhostFill", lpRoot, style.lpGhostColor, style.lpGhostFillSprite, style.lpFillMaterial, style.barFillImageType, new Vector2(layout.barX, 0f), new Vector2(layout.lpBarWidth, layout.lpBarHeight));
            lpGhostMask.gameObject.SetActive(style.enableLpGhostFill);
            RectTransform lpFillMask = CreateMaskedFill("LpFillMaskRoot", "LpFill", lpRoot, style.lpColor, style.lpBarFillSprite, style.lpFillMaterial, style.barFillImageType, new Vector2(layout.barX, 0f), new Vector2(layout.lpBarWidth, layout.lpBarHeight));
            lpGhostMask.SetAsLastSibling();
            lpFillMask.SetAsLastSibling();

            if (style.lpBarFrameSprite != null)
            {
                Image frame = CreateImage("LpFrame", lpRoot, style.barFrameColor, style.lpBarFrameSprite, style.barFrameMaterial, style.barFrameImageType);
                SetRect(frame.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(layout.barX, 0f), new Vector2(layout.lpBarWidth, layout.lpBarHeight + 8f));
            }
            lpBack.SetAsFirstSibling();

            RectTransform loadRoot = CreateUIObject("LoadRoot", root.transform).GetComponent<RectTransform>();
            SetRect(loadRoot, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, layout.loadTextPosition, layout.loadTextSize);

            float loadFontSize = Mathf.RoundToInt(layout.loadFontSize * style.fontScale);
            TMP_Text loadPrefix = CreateText("LoadPrefixText", loadRoot, style.ResolveLoadPrefixText(), Mathf.RoundToInt(loadFontSize), TextAlignmentOptions.Left, style.playerLoadLabelColor, style.ResolveLoadLabelFont());
            loadPrefix.characterSpacing = layout.loadCharacterSpacing;
            SetRect(loadPrefix.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(style.loadLabelSegmentWidth, layout.loadTextSize.y));
            ApplyTextAspect(loadPrefix, style.ResolveLoadLabelAspect());

            TMP_Text loadNumber = CreateText("LoadNumberText", loadRoot, "51", Mathf.RoundToInt(loadFontSize), TextAlignmentOptions.Right, style.playerLoadNumberColor, style.ResolveLoadNumberFont());
            loadNumber.characterSpacing = layout.loadCharacterSpacing;
            SetRect(loadNumber.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(style.loadLabelSegmentWidth, 0f), new Vector2(style.loadNumberSegmentWidth, layout.loadTextSize.y));
            ApplyTextAspect(loadNumber, style.ResolveLoadNumberAspect());

            TMP_Text loadPercent = CreateText("LoadPercentText", loadRoot, style.ResolveLoadPercentSymbolText(), Mathf.RoundToInt(loadFontSize), TextAlignmentOptions.Left, style.playerLoadPercentColor, style.ResolveLoadNumberFont());
            loadPercent.characterSpacing = layout.loadCharacterSpacing;
            SetRect(loadPercent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(style.loadLabelSegmentWidth + style.loadNumberSegmentWidth + layout.loadPercentOffsetX, 0f), new Vector2(style.loadPercentSegmentWidth, layout.loadTextSize.y));
            ApplyTextAspect(loadPercent, style.ResolveLoadNumberAspect());

            TMP_Text loadStatus = CreateText("LoadStatusText", loadRoot, style.ResolveLoadNormalText(), Mathf.RoundToInt(loadFontSize), TextAlignmentOptions.Left, style.loadNormalStatusColor, style.ResolveLoadStatusFont());
            loadStatus.characterSpacing = layout.loadCharacterSpacing;
            loadStatus.overflowMode = TextOverflowModes.Overflow;
            float loadStatusX = style.loadLabelSegmentWidth + style.loadNumberSegmentWidth + style.loadPercentSegmentWidth + style.loadStatusSegmentGap + layout.loadStatusOffsetX;
            SetRect(loadStatus.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(loadStatusX, 0f), new Vector2(Mathf.Max(120f, layout.loadTextSize.x - loadStatusX), layout.loadTextSize.y));
            ApplyTextAspect(loadStatus, style.ResolveLoadStatusAspect());

            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = style.overallAlpha;

            SkyPrisonPlayerHUDView_V4_StyleDriven view = root.AddComponent<SkyPrisonPlayerHUDView_V4_StyleDriven>();
            view.Bind(hpFillMask, hpDelayMask, hpLabel, lpFillMask, lpLabel, null, slotIcons);
            view.BindLoadSegments(loadPrefix, loadNumber, loadPercent, loadStatus);
            view.BindLpGhost(lpGhostMask);
            view.ApplyStyleTiming(style);
            view.SetHp(100f, 100f);
            view.SetHpDelay(100f, 100f);
            view.SetLp(100f, 100f);
            view.SetLoad(0.51f);

            SetLayerRecursive(root, LayerMask.NameToLayer("UI"));
            ApplyModuleBlendMaterial(root, style.defaultUIMaterial, style.ResolvePlayerStatusModuleMaterial(), style.ResolvePlayerStatusBlendMode(), style.applyModuleMaterialToTMPText);
            return root;
        }

        public static GameObject BuildQuickSlotsHUDObject(SkyPrisonHUDLayoutContextV1 context, SkyPrisonHUDStyleProfile_V1 style)
        {
            SkyPrisonQuickSlotsHUDVisualLayoutV1 layout = context.quickSlots ?? new SkyPrisonQuickSlotsHUDVisualLayoutV1();

            // V21: default quick-slot cards must be tall enough to read and must stay independent from HP/LP.
            // Older test profiles used a very thin slot height; keep user width/gap but raise cramped default height.
            Vector2 effectiveSlotSize = layout.slotSize;
            if (effectiveSlotSize.y < 60f)
                effectiveSlotSize.y = Mathf.Max(effectiveSlotSize.y * 2f, 72f);

            Vector2 effectiveRootSize = layout.rootSize;
            float requiredWidth = 4f * effectiveSlotSize.x + 3f * layout.slotGap;
            effectiveRootSize.x = Mathf.Max(effectiveRootSize.x, requiredWidth);
            effectiveRootSize.y = Mathf.Max(effectiveRootSize.y, effectiveSlotSize.y);

            GameObject root = CreateUIObject("PF_UIModule_QuickSlots_LeftBottom_V32_Independent", null);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.sizeDelta = effectiveRootSize;

            Image[] slotIcons = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                float x = i * (effectiveSlotSize.x + layout.slotGap);
                // 跟角色面板装备列表同一套视觉语言：填充色不要死黑，用淡一点的灰白；
                // 四个角落各一个小方块■代替满框实线边——这里直接覆盖 style.slotBackColor，
                // 因为原来的深色/无边框跟新版装备面板的列表条风格不统一。
                Color lighterFill = new Color(1f, 1f, 1f, 0.12f);
                Image slotBack = CreateImage($"Slot_{i + 1:00}", root.transform, lighterFill, style.quickSlotBackSprite, style.defaultUIMaterial, style.slotImageType);
                SetRect(slotBack.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, 0f), effectiveSlotSize);

                if (style.quickSlotFrameSprite != null)
                {
                    Image frame = CreateImage("SlotFrame", slotBack.transform, style.slotFrameColor, style.quickSlotFrameSprite, style.slotFrameMaterial, style.slotImageType);
                    SetRect(frame.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                }

                BuildQuickSlotCornerMarkers(slotBack.transform);

                TMP_Text number = CreateText("SlotNumber", slotBack.transform, (i + 1).ToString(), Mathf.RoundToInt(layout.slotNumberFontSize * style.fontScale), TextAlignmentOptions.Left, style.slotNumberColor, style.numberFont != null ? style.numberFont : style.labelFont);
                SetRect(number.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(7f, 4f), new Vector2(24f, 12f));
                ApplyTextAspect(number, style.ResolveGlobalNumberAspect());

                Image icon = CreateImage("Icon", slotBack.transform, new Color(1f, 1f, 1f, 0f), null, style.defaultUIMaterial, Image.Type.Simple);
                icon.enabled = false;
                SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * Mathf.Min(layout.iconSize, effectiveSlotSize.y - 16f));
                slotIcons[i] = icon;
            }

            CanvasGroup group = root.AddComponent<CanvasGroup>();
            group.alpha = style.overallAlpha;

            SkyPrisonPlayerHUDView_V4_StyleDriven view = root.AddComponent<SkyPrisonPlayerHUDView_V4_StyleDriven>();
            view.Bind(null, null, null, null, null, null, slotIcons);
            view.ApplyStyleTiming(style);

            SetLayerRecursive(root, LayerMask.NameToLayer("UI"));
            ApplyModuleBlendMaterial(root, style.defaultUIMaterial, style.ResolveQuickSlotsModuleMaterial(), style.ResolveQuickSlotsBlendMode(), style.applyModuleMaterialToTMPText);
            return root;
        }

        private static void ApplyModuleBlendMaterial(GameObject root, Material defaultMaterial, Material moduleMaterial, SkyPrisonHUDBlendModeV1 blendMode, bool includeTMPText)
        {
            if (root == null)
                return;

            bool hasExplicitModuleMaterial = moduleMaterial != null;
            bool needsBlendMaterial = blendMode != SkyPrisonHUDBlendModeV1.Normal;

            // V87: the workbench preview must stay visible and stable.
            // The module RT path is for Scene installation; in the workbench we draw the raw UGUI module.
            // Do not apply chromatic/hard-light per-node materials here either, because some HUD shaders depend on
            // scene camera buffers and can make the editor preview disappear.
            if (SuppressModulePostProcessForWorkbenchPreview)
                return;

            if (!hasExplicitModuleMaterial && !needsBlendMaterial)
                return;

            Material fallbackMaterial = moduleMaterial != null ? moduleMaterial : defaultMaterial;

            // V88:
            // Do NOT route HUD Chromatic through the legacy module RT / overlay path here.
            // The project standard is now a single direct-material route:
            // StyleProfile module material -> actual Image/RawImage/TMP materials.
            //
            // Reason:
            // The old chromatic offset overlay duplicated PlayerStatusArea into Red/Cyan UI trees.
            // That route has been deprecated by SkyPrisonHUDChromaticOffsetOverlayPresenter V6.
            // If this builder returns early into the module RT path, the actual HpBack/HpFill/TMP nodes
            // can remain on UI/Default, making shader/material edits appear to have no effect.
            //
            // Therefore even HUD Chromatic materials are applied directly to every Graphic below.
            // SoftLight/HardLight are also kept on the direct route for now to avoid creating another
            // parallel rendering path while the HUD chromatic chain is being stabilized.
            Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                    continue;

                bool isTMP = graphic is TMP_Text;

                // V52: non-Normal blend must also affect TMP text.
                // The old experimental switch only allowed explicit text material override; that made Scene installs show blended bars but normal white text.
                // For TMP we use a UI-compatible atlas material instead of directly forcing a sprite material onto TMP SDF, so the glyph atlas is still sampled.
                if (isTMP)
                {
                    TMP_Text tmpText = graphic as TMP_Text;
                    bool shouldAffectTMP = includeTMPText || blendMode != SkyPrisonHUDBlendModeV1.Normal;
                    if (!shouldAffectTMP)
                        continue;

                    Material tmpSource = hasExplicitModuleMaterial ? moduleMaterial : fallbackMaterial;
                    Material tmpResolved = CreateTMPBlendMaterialInstance(tmpText, blendMode, tmpSource);
                    if (tmpResolved != null)
                    {
                        // TMP does not reliably use Graphic.material as the final font material.
                        // The effective route is TMP_Text.fontSharedMaterial / fontMaterial.
                        tmpText.fontSharedMaterial = tmpResolved;
                        tmpText.fontMaterial = tmpResolved;
                        graphic.material = tmpResolved;
                    }
                    continue;
                }

                // V50: module/global material is a real module-level pass.
                // It must affect the whole module, not only objects that happened to use defaultUIMaterial.
                Material source = hasExplicitModuleMaterial ? moduleMaterial : ResolveGraphicSourceMaterial(graphic, fallbackMaterial);
                Material resolved = CreateBlendMaterialInstance(source, blendMode);
                if (resolved == null)
                    continue;

                graphic.material = resolved;
                ConfigureUIImageRectProperties(graphic, resolved);
            }

            // V87: Image chromatic aberration is handled inside the UI shader.
            // Do not create RGB proxy Images; they become extra Canvas layers and contaminate hard-light blending.
        }


        private static void ConfigureUIImageRectProperties(Graphic graphic, Material material)
        {
            if (graphic == null || material == null)
                return;

            RectTransform rt = graphic.rectTransform;
            if (rt == null)
                return;

            Rect rect = rt.rect;
            if (material.HasProperty("_HUDRectMinMax"))
                material.SetVector("_HUDRectMinMax", new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax));

            if (material.HasProperty("_HUDRectSize"))
                material.SetVector("_HUDRectSize", new Vector4(Mathf.Max(1f, rect.width), Mathf.Max(1f, rect.height), 1f / Mathf.Max(1f, rect.width), 1f / Mathf.Max(1f, rect.height)));
        }

        private static Material ResolveGraphicSourceMaterial(Graphic graphic, Material fallbackMaterial)
        {
            if (graphic == null)
                return fallbackMaterial;

            Material current = graphic.material;
            if (current == null)
                return fallbackMaterial;

            string name = current.name;
            bool unityDefault = !string.IsNullOrEmpty(name) && name.Contains("Default UI");
            return unityDefault ? fallbackMaterial : current;
        }

        private static Material CreateBlendMaterialInstance(Material source, SkyPrisonHUDBlendModeV1 blendMode)
        {
            Material material = null;

            // V87: if the user selected an older HUD Chromatic material, rebuild the runtime instance
            // with the V57 image shader. V55 sampled RGB at shifted UVs, so flat HP/LP bars had
            // almost no visible separation. V57 uses channel-shifted passes, which also affects
            // solid color rectangles and masked bars.
            if (IsChromaticHUDMaterial(source))
            {
                Shader chromaticShader = Shader.Find("Sky Prison/UI/HUD Chromatic V83");
                material = chromaticShader != null ? new Material(chromaticShader) : new Material(source);
                CopyChromaticProperties(source, material);
            }
            else if (source != null)
            {
                material = new Material(source);
            }
            else
            {
                Shader shader = Shader.Find("Sky Prison/UI/HUD Blend V83");
                if (shader != null)
                    material = new Material(shader);
            }

            if (material == null)
                return null;

            material.name = $"{material.name}_HUD_V83_{blendMode}";
            // V51: do not mark generated blend materials as DontSave.
            // Scene-installed HUD and saved prefabs must keep their material instances;
            // otherwise the workbench preview can show the blend correctly while the Scene install falls back to default UI materials.
            material.hideFlags = HideFlags.None;
            ConfigureBlendMaterial(material, blendMode);
            return material;
        }

        private static Material CreateTMPBlendMaterialInstance(TMP_Text text, SkyPrisonHUDBlendModeV1 blendMode, Material moduleSourceMaterial)
        {
            if (text == null)
                return null;

            // V87: TMP text needs its own chromatic shader when the module/global material is the chromatic UI shader.
            // The regular Image chromatic shader cannot safely render TMP SDF text.
            bool useChromaticTMP = IsChromaticHUDMaterial(moduleSourceMaterial);
            Shader shader = Shader.Find(useChromaticTMP ? "Sky Prison/UI/HUD TMP Chromatic V83" : "Sky Prison/UI/HUD TMP Blend V83");
            Material material = shader != null ? new Material(shader) : null;
            if (material == null && text.fontSharedMaterial != null)
                material = new Material(text.fontSharedMaterial);
            if (material == null)
                return null;

            material.name = $"{text.name}_TMP_HUD_V83_{blendMode}";
            material.hideFlags = HideFlags.None;

            Texture atlas = null;
            if (text.font != null)
                atlas = text.font.atlasTexture;
            if (atlas != null && material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", atlas);

            // Keep TMP face color white here. The real per-text color is still carried by TMP vertex colors.
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);

            if (useChromaticTMP)
                CopyChromaticProperties(moduleSourceMaterial, material);

            ConfigureBlendMaterial(material, blendMode);
            return material;
        }

        private static bool IsChromaticHUDMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            string shaderName = material.shader.name;
            return !string.IsNullOrEmpty(shaderName) && shaderName.Contains("HUD Chromatic");
        }

        private static void CopyChromaticProperties(Material source, Material target)
        {
            if (source == null || target == null)
                return;

            CopyFloat(source, target, "_ChromaticAmount");
            CopyFloat(source, target, "_ChromaticAngle");
            CopyFloat(source, target, "_ChromaticSoftness");
            CopyFloat(source, target, "_ChromaticAlphaBoost");
            CopyFloat(source, target, "_GeometryFringeStrength");
            CopyFloat(source, target, "_GeometryFringeWidth");
            CopyFloat(source, target, "_GeometryFringeOutputBoost");
            CopyFloat(source, target, "_GeometryFringeDebug");
            CopyColor(source, target, "_RedTint");
            CopyColor(source, target, "_GreenTint");
            CopyColor(source, target, "_BlueTint");
            CopyFloat(source, target, "_HUDBrightness");
            CopyFloat(source, target, "_HUDContrast");
            CopyFloat(source, target, "_HUDEmission");
            CopyFloat(source, target, "_HUDLightStrength");
            CopyFloat(source, target, "_HUDSourcePreserve");
            CopyFloat(source, target, "_HUDSourceFloor");
            CopyFloat(source, target, "_HUDSaturation");
            CopyFloat(source, target, "_UsePSBackdropRecipe");
            CopyFloat(source, target, "_BackdropSampleStrength");
            CopyFloat(source, target, "_BackdropExposure");
            CopyColor(source, target, "_BackdropFallbackColor");
        }

        private static void CopyFloat(Material source, Material target, string property)
        {
            if (source.HasProperty(property) && target.HasProperty(property))
                target.SetFloat(property, source.GetFloat(property));
        }

        private static void CopyColor(Material source, Material target, string property)
        {
            if (source.HasProperty(property) && target.HasProperty(property))
                target.SetColor(property, source.GetColor(property));
        }

        private static void ConfigureBlendMaterial(Material material, SkyPrisonHUDBlendModeV1 blendMode)
        {
            if (material == null)
                return;

            // ShaderLab Blend factors are driven by numeric properties in Sky Prison/UI/HUD Blend V49.
            // Values follow UnityEngine.Rendering.BlendMode / BlendOp enums, written as floats to avoid an editor-only dependency.
            float src = 5f; // SrcAlpha
            float dst = 10f; // OneMinusSrcAlpha
            float op = 0f; // Add

            switch (blendMode)
            {
                case SkyPrisonHUDBlendModeV1.Multiply:
                    src = 2f;  // DstColor
                    dst = 10f; // OneMinusSrcAlpha
                    op = 0f;
                    break;
                case SkyPrisonHUDBlendModeV1.Add:
                    src = 5f; // SrcAlpha
                    dst = 1f; // One
                    op = 0f;
                    break;
                case SkyPrisonHUDBlendModeV1.Subtract:
                    src = 5f; // SrcAlpha
                    dst = 1f; // One
                    op = 2f;  // ReverseSubtract
                    break;
                case SkyPrisonHUDBlendModeV1.SoftLight:
                    // V87: keep SoftLight / HardLight on the same stable alpha-blend path as Normal.
                    // The previous Screen-like blend factors stacked badly with Chromatic materials in CanvasRenderer / Preview RT,
                    // making the workbench go red and Scene installs wash toward white.
                    // The shader now shapes the source color locally; no destination-color blend factors are used here.
                    src = 5f;  // SrcAlpha
                    dst = 10f; // OneMinusSrcAlpha
                    op = 0f;
                    break;
                case SkyPrisonHUDBlendModeV1.HardLight:
                    src = 5f;  // SrcAlpha
                    dst = 10f; // OneMinusSrcAlpha
                    op = 0f;
                    break;
            }

            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", src);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", dst);
            if (material.HasProperty("_BlendOp")) material.SetFloat("_BlendOp", op);
            if (material.HasProperty("_SkyPrisonBlendMode")) material.SetFloat("_SkyPrisonBlendMode", (float)blendMode);
        }

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                obj.transform.SetParent(parent, false);
            SetLayerRecursive(obj, LayerMask.NameToLayer("UI"));
            return obj;
        }

        // 快捷物品槽四个角落各画一个小方块■，跟角色面板装备列表条同一套边框语言，
        // 代替满圈实线边——纯装饰，不接收射线检测。
        private static void BuildQuickSlotCornerMarkers(Transform parent)
        {
            const float cornerSize = 5f;
            Color cornerColor = new Color(1f, 1f, 1f, 0.4f);
            AddQuickSlotCorner(parent, new Vector2(0f, 0f), cornerSize, cornerColor);
            AddQuickSlotCorner(parent, new Vector2(1f, 0f), cornerSize, cornerColor);
            AddQuickSlotCorner(parent, new Vector2(0f, 1f), cornerSize, cornerColor);
            AddQuickSlotCorner(parent, new Vector2(1f, 1f), cornerSize, cornerColor);
        }

        private static void AddQuickSlotCorner(Transform parent, Vector2 anchor, float size, Color color)
        {
            Image corner = CreateImage("Corner", parent, color, null, null, Image.Type.Simple);
            SetRect(corner.rectTransform, anchor, anchor, anchor, Vector2.zero, new Vector2(size, size));
        }

        private static Image CreateImage(string name, Transform parent, Color color, Sprite sprite, Material material, Image.Type imageType)
        {
            GameObject obj = CreateUIObject(name, parent);
            Image image = obj.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = sprite;
            image.material = material;
            image.type = sprite != null ? imageType : Image.Type.Simple;

            // V20 PF-first template rule:
            // A newly created HUD PF must remain visually editable even before final sprites are assigned.
            // Missing sprites therefore render as solid-color prototype Images instead of disappearing after Save/Reload.
            image.color = color;
            image.enabled = true;

            return image;
        }

        private static RectTransform CreateMaskedFill(string maskName, string fillName, Transform parent, Color fillColor, Sprite fillSprite, Material fillMaterial, Image.Type fillImageType, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject maskObject = CreateUIObject(maskName, parent);
            RectTransform maskRect = maskObject.GetComponent<RectTransform>();
            SetRect(maskRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), anchoredPosition, sizeDelta);

            RectMask2D mask = maskObject.GetComponent<RectMask2D>();
            if (mask == null)
                maskObject.AddComponent<RectMask2D>();

            Image fill = CreateImage(fillName, maskObject.transform, fillColor, fillSprite, fillMaterial, fillImageType);
            // The fill image keeps the full bar size. HP/LP change only resizes maskRect, so image details are clipped, not compressed.
            SetRect(fill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, sizeDelta);
            return maskRect;
        }

        private static Image CreateImageRect(string name, Transform parent, Color color, Sprite sprite, Material material, Image.Type imageType, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            Image image = CreateImage(name, parent, color, sprite, material, imageType);
            SetRect(image.rectTransform, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
            return image;
        }

        private static void ApplyTextAspect(TMP_Text text, Vector2 aspect)
        {
            if (text == null)
                return;

            aspect.x = Mathf.Clamp(aspect.x <= 0f ? 1f : aspect.x, 0.35f, 3.00f);
            aspect.y = Mathf.Clamp(aspect.y <= 0f ? 1f : aspect.y, 0.35f, 3.00f);
            text.rectTransform.localScale = Vector3.one;

            global::SkyPrisonTMPGlyphAspect glyphAspect = text.GetComponent<global::SkyPrisonTMPGlyphAspect>();
            if (glyphAspect == null)
                glyphAspect = text.gameObject.AddComponent<global::SkyPrisonTMPGlyphAspect>();

            glyphAspect.SetAspect(aspect);
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, int fontSize, TextAlignmentOptions alignment, Color color, TMP_FontAsset font)
        {
            GameObject obj = CreateUIObject(name, parent);
            TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = Mathf.Max(1, fontSize);
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            tmp.enableWordWrapping = false;
            if (font != null)
                tmp.font = font;
            return tmp;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        public static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null || layer < 0)
                return;

            root.layer = layer;
            Transform t = root.transform;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i).gameObject, layer);
        }

    }
}
