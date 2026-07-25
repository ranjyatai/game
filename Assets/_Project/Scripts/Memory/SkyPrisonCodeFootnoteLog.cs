#if UNITY_EDITOR
namespace SkyPrison.Memory
{
    /// <summary>
    /// Sky Prison project memory / engineering roadmap.
    /// This file is documentation only.
    /// It must not be attached to GameObjects, create menus, modify assets, or affect runtime.
    /// </summary>
    internal static class SkyPrisonCodeFootnoteLog
    {
        // ============================================================
        // 00. GLOBAL EDITING RULES / 全局修改铁则
        // ============================================================
        //
        // Before changing code:
        //
        // 1. Confirm the real entry point.
        // 2. Confirm the asset / prefab / runtime chain.
        // 3. State which layer is being changed.
        // 4. State which stable chains must not be touched.
        // 5. State how old scripts / old nodes / old materials / old shaders are cleaned.
        // 6. Use minimal diffs.
        // 7. Do not create parallel fallback systems unless explicitly approved.
        //
        // Forbidden:
        //
        // - Do not bypass Workbench / PF / StyleProfile routes.
        // - Do not overwrite whole files casually when a local patch is enough.
        // - Do not turn temporary debug values into official defaults.
        // - Do not fix display problems by mutating unrelated backend logic.
        // - Do not touch already stable PlayerStatusArea / HP / LP / damage-lag chains unless explicitly required.
        //
        // Versioning:
        //
        // - New delivered scripts must include version labels in filenames or comments.
        // - Keep Unity-facing class names unchanged when prefab / serialized references depend on them.

        // ============================================================
        // 01. UI / HUD / PF MAIN CHAIN
        // ============================================================
        //
        // Confirmed official chain:
        //
        // UI Workbench
        //   -> SkyPrisonHUDStyleProfile_V1 / GlobalStyleSettings / material parameters
        //   -> Save PF_SkyPrisonBattleHUD.prefab
        //   -> SkyPrisonUIRuntimeDriver instantiates the PF
        //   -> Game view shows the runtime instance of the PF
        //
        // Debug priority for HUD issues:
        //
        // 1. What does the Workbench setting say?
        // 2. What is actually saved inside PF_SkyPrisonBattleHUD.prefab?
        // 3. Does RuntimeDriver merely instantiate the PF?
        // 4. Does the runtime instance match the saved PF?
        // 5. Only then inspect shader / material / live runtime nodes.
        //
        // Important:
        //
        // - Game-time HUD nodes such as HpBack / HpFill are not the source of truth.
        // - The saved PF is the source of truth.
        // - Runtime fixes must not be used to hide PF save-chain mistakes.

        // ============================================================
        // 02. UI WORKBENCH
        // ============================================================
        //
        // Main file:
        //
        // Assets/_Project/Scripts/Editor/UIWorkbench/SkyPrisonUIWorkbenchWindow.cs
        //
        // Confirmed meaning:
        //
        // - The Workbench edits an in-memory instance.
        // - Only Save writes back to the prefab asset.
        // - PF is the real UI delivery asset.
        //
        // Key asset:
        //
        // Assets/_Project/Prefabs/UI/HUD/PF_SkyPrisonBattleHUD.prefab
        //
        // When changing UI appearance:
        //
        // - Prefer fixing Workbench -> PF save flow.
        // - Do not patch the runtime instance first.

        // ============================================================
        // 03. HUD STYLE PROFILE / MATERIAL ROUTE
        // ============================================================
        //
        // Main file:
        //
        // Assets/_Project/Scripts/Core/Runtime/UI/HUD/SkyPrisonHUDStyleProfile.cs
        //
        // Important fields:
        //
        // - globalUIMaterial
        // - defaultUIMaterial
        // - playerStatusOverrideGlobalMaterial
        // - playerStatusModuleMaterial
        // - quickSlotsOverrideGlobalMaterial
        // - quickSlotsModuleMaterial
        // - applyModuleMaterialToTMPText
        // - globalUIBlendMode
        // - playerStatusBlendMode
        // - quickSlotsBlendMode
        //
        // Rule:
        //
        // - Chromatic / blend parameters should come from Workbench / Profile / material assets.
        // - Do not hardcode chromatic values in Builder, RuntimeDriver, or Presenter.
        // - The PF should save the correct module-level post-process configuration.

        // ============================================================
        // 04. UI CHROMATIC ABERRATION / 色收差
        // ============================================================
        //
        // Deprecated fake chromatic route:
        //
        // Assets/_Project/Scripts/Core/Runtime/UI/Core/SkyPrisonHUDChromaticOffsetOverlayPresenter.cs
        //
        // Old behavior:
        //
        // - Creates __ChromaticOffsetOverlay_PlayerStatusArea.
        // - Creates RedChannel / CyanChannel.
        // - Duplicates PlayerStatusArea nodes.
        // - Tints duplicated UI red / cyan and offsets them.
        //
        // Verdict:
        //
        // - This is fake chromatic aberration.
        // - It is not true RGB channel splitting.
        // - It is deprecated.
        //
        // Current rule:
        //
        // - SkyPrisonHUDChromaticOffsetOverlayPresenter.cs may only act as Legacy Cleaner.
        // - It must not recreate RedChannel / CyanChannel overlay trees.
        //
        // Official chromatic route:
        //
        // Workbench / StyleProfile / material parameters
        //   -> PF_SkyPrisonBattleHUD.prefab module-level RT post-process configuration
        //   -> Sky Prison/UI/HUD Module PostProcess Clean V32
        //   -> RuntimeDriver instantiates PF
        //
        // Important:
        //
        // - If HpBack in Play mode shows Material=None / UI/Default, inspect the saved PF first.
        // - Do not add runtime probe / repair scripts as the official solution.

        // ============================================================
        // 05. PLAYER HUD BUILDER
        // ============================================================
        //
        // Main file:
        //
        // Assets/_Project/Scripts/Core/Runtime/UI/HUD/SkyPrisonPlayerHUDBuilder.cs
        //
        // Role:
        //
        // - Builds PlayerStatus / QuickSlots UI structures.
        // - Should serve PF generation / preview / installer routes.
        //
        // Rule:
        //
        // - Do not use Builder as a runtime patch layer if the PF is already saved incorrectly.
        // - If the PF is built through Builder, then material/post-process assignment must be correct before saving the PF.
        // - Keep HP / LP / damage-lag structure stable.

        // ============================================================
        // 06. PLAYER HUD VIEW
        // ============================================================
        //
        // Main file:
        //
        // Assets/_Project/Scripts/Core/Runtime/UI/HUD/SkyPrisonPlayerHUDView.cs
        //
        // Role:
        //
        // - Runtime HP / LP / Load binding.
        // - HP current fill animation.
        // - HP damage-lag / virtual blood behavior.
        // - LP animation.
        // - Load text display.
        //
        // Rule:
        //
        // - It should not own chromatic material routing.
        // - Do not change HP / LP / damage-lag behavior when fixing shader / material / PF save problems.

        // ============================================================
        // 07. RUNTIME DRIVER
        // ============================================================
        //
        // Main file:
        //
        // Assets/_Project/Scripts/Core/Runtime/UI/Core/SkyPrisonUIRuntimeDriver.cs
        //
        // Confirmed role:
        //
        // - Instantiates PF_SkyPrisonBattleHUD.prefab or fallback prefab.
        // - Should not be used to repair materials after instantiation unless explicitly required.
        //
        // Dangerous old menu:
        //
        // Tools/Sky Prison/UI/Create Runtime UI Driver In Scene
        //
        // Old issue:
        //
        // - It could create SkyPrisonUISystem.
        // - RuntimeDriver could spawn an extra HUD under SkyPrisonRuntimeUIRoot.
        // - This caused duplicated HUD layers.
        //
        // Rule:
        //
        // - Keep RuntimeDriver simple.
        // - Remove / disable dangerous editor menu entries that create obsolete runtime UI drivers in Scene.

        // ============================================================
        // 08. HUD ROOT / PREFAB METADATA
        // ============================================================
        //
        // Files:
        //
        // Assets/_Project/Scripts/Core/Runtime/UI/Core/SkyPrisonHUDRoot.cs
        // Assets/_Project/Scripts/Core/Runtime/UI/Core/SkyPrisonHUDRuntimeTypes.cs
        // Assets/_Project/Scripts/Core/Runtime/UI/Core/SkyPrisonUIPrefabMetadata.cs
        //
        // Roles:
        //
        // - HUDRoot controls visibility / scale / display mode.
        // - RuntimeTypes defines HUD area ids and display modes.
        // - PrefabMetadata stores identity / visibility / input / sorting data.
        //
        // Rule:
        //
        // - These are not chromatic material owners.
        // - Do not use them to patch visual effects.

        // ============================================================
        // 09. TEMP DEBUG / REPAIR SCRIPT CLEANUP
        // ============================================================
        //
        // These scripts must not remain as official chains:
        //
        // - SkyPrisonHUDRuntimeMaterialProbeTool_V1.cs
        // - SkyPrisonHUDRuntimeMaterialProbeTool_V2_NoFilter.cs
        // - SkyPrisonHUDMaterialShaderRepairTool_V1.cs
        //
        // Rule:
        //
        // - Short-term diagnosis only.
        // - Delete after use, or move out of official folders.
        // - Do not let temporary tools become the architecture.

        // ============================================================
        // 10. WHEN CONTINUING A PREVIOUS TOPIC
        // ============================================================
        //
        // First step:
        //
        // - Read this file.
        // - Restore the confirmed chain before suggesting changes.
        //
        // For UI / HUD topics:
        //
        // - Start from Workbench -> PF -> Runtime.
        // - Do not start from live GameObject symptoms.
        //
        // For chromatic aberration:
        //
        // - Check whether the old overlay is gone.
        // - Check PF module post-process configuration.
        // - Then check shader behavior.
        //
        // For HP / LP / damage-lag:
        //
        // - Preserve PlayerHUDView timing and mask structure.
        // - Do not mix visual effect repair with HP logic repair.

        // ============================================================
        // 11. HUD PF RESOURCES MIRROR / HUD PF 的 Resources 镜像
        // ============================================================
        //
        // Confirmed risk:
        //
        // - Project search can show more than one PF_SkyPrisonBattleHUD prefab.
        // - One is the main editable PF.
        // - Another may be a Resources mirror used for Build / runtime fallback.
        //
        // Main editable PF:
        //
        // Assets/_Project/Prefabs/UI/HUD/PF_SkyPrisonBattleHUD.prefab
        //
        // Possible Resources mirror:
        //
        // Assets/(any folder)/Resources/UI/HUD/PF_SkyPrisonBattleHUD.prefab
        //
        // Runtime fallback path seen in RuntimeDriver:
        //
        // UI/HUD/PF_SkyPrisonBattleHUD
        //
        // Important rule:
        //
        // - The main PF is the editing source of truth.
        // - The Resources mirror is only a runtime/build fallback mirror.
        // - Do not manually edit the Resources mirror as the source.
        // - When the Workbench saves the main PF, it must also sync/overwrite the Resources mirror.
        //
        // Debug priority when Play mode HUD differs from the PF:
        //
        // 1. Open the main PF and inspect the module/post-process state.
        // 2. Open the Resources mirror PF and inspect the same module/post-process state.
        // 3. If the main PF is correct but runtime is wrong, suspect the Resources mirror first.
        // 4. Only then inspect RuntimeDriver prefab loading order.
        //
        // Example symptom:
        //
        // - Main PF PlayerStatusArea has the correct module post-process configuration.
        // - Runtime PlayerStatusArea is missing it or uses old material state.
        // - Project search shows two PF_SkyPrisonBattleHUD assets.
        //
        // Likely cause:
        //
        // - RuntimeDriver loaded an unsynced Resources mirror.
        //
        // Correct fix:
        //
        // - Update SkyPrisonUIWorkbenchWindow.cs SaveCurrentPrefab() flow.
        // - After saving the main PF, copy/sync it to the Resources mirror path.
        // - Do not patch runtime nodes.

        // ============================================================
        // 12. HARD RULE: HUD CHROMATIC MUST BE TRUE CHROMATIC ABERRATION
        // ============================================================
        //
        // User requirement:
        //
        // - HUD 色收差必须是真色收差。
        // - Do not replace it with fake red/blue decorative tinting.
        // - Do not use copied Red/Cyan UI trees.
        // - Do not use per-Image rectangle edge coloring as the official solution.
        //
        // Definition of true chromatic aberration in this project:
        //
        // - First render the whole HUD module into one texture / RenderTexture.
        // - Then perform RGB channel offset sampling on that texture:
        //
        //   R = sample(uv - offset).r
        //   G = sample(uv).g
        //   B = sample(uv + offset).b
        //
        // - The result must affect the module as a whole:
        //   text, bars, frames, quick slots, icons and other module contents split together.
        //
        // Explicitly rejected fake routes:
        //
        // 1. SkyPrisonHUDChromaticOffsetOverlayPresenter old route:
        //    - duplicate PlayerStatusArea
        //    - RedChannel / CyanChannel
        //    - tinted copies with positional offsets
        //
        // 2. Per-Image Source Image=None shader trick:
        //    - sampling the default white texture
        //    - coloring left/right rectangle edges red/blue
        //    - RectEdgeFringeStrength / RectEdgeFringeWidth / RectEdgeAlpha as official effect
        //
        // 3. Saving M_HUD_Chromatic_Auto onto every HpBack / HpFill Image and calling it done.
        //
        // Correct official route:
        //
        // Workbench setting enables chromatic
        //   -> PF saves module-level post-process configuration
        //   -> Runtime module post-process renderer captures PlayerStatusArea / QuickSlotsArea to RT
        //   -> HUD Module PostProcess shader performs true RGB channel split on the RT
        //   -> RawImage displays the processed module result
        //
        // Debug success condition:
        //
        // - Do not judge success by HpBack material.
        // - Judge success by whether the module is rendered through RT post-process.
        // - The visible result should be unified module-wide RGB separation, not colored end caps.
        //
        // If future implementation starts drifting toward fake tinting:
        //
        // - Stop.
        // - Return to module RT post-process.
        // - Keep this rule as higher priority than short-term visual hacks.

        // ============================================================
        // 13. FINAL NOTE: HUD TRUE CHROMATIC ABERRATION IMPLEMENTATION
        // ============================================================
        //
        // Current accepted solution:
        //
        // - HUD chromatic aberration is now module-level true RT post-process.
        // - The effect is considered valid only when the whole module is captured into RT first,
        //   then the RT is sampled with RGB channel offsets.
        //
        // Accepted chain:
        //
        // PlayerStatusArea
        //   -> SkyPrisonHUDModulePostProcessRenderer
        //   -> hidden SourceRoot / SourceCanvas / SourceCamera
        //   -> RT_HUDModule_PlayerStatusArea
        //   -> __ModulePostProcessOutput_V87 RawImage
        //   -> HUD Module PostProcess shader true RGB split
        //
        // Runtime dynamic binding:
        //
        // - Dynamic HP / LP / virtual damage-lag must still be driven by SkyPrisonPlayerHUDView.
        // - The key fix was a bridge PlayerHUDView on the displayed PlayerStatusArea.
        // - Runtime binders can find the bridge in the normal visible HUD hierarchy.
        // - The bridge is rebound to the hidden RT SourceRoot so it drives the live source UI.
        //
        // Accepted runtime diagnostic:
        //
        // Runtime Live Binding Refreshed = true
        // Runtime Live Binding View Count = 1
        // Runtime Live Binding Status = Rebound 1 PlayerHUDView(s) to RT source
        //
        // Main files for this chain:
        //
        // Assets/_Project/Scripts/UI/HUD/SkyPrisonHUDModulePostProcessRenderer.cs
        // Assets/_Project/Shaders/HUD/SkyPrisonHUDModulePostProcess.shader
        // Assets/_Project/Scripts/Editor/UIWorkbench/SkyPrisonUIWorkbenchWindow.cs
        //
        // Quality tuning:
        //
        // - Workbench chromatic intensity should map to a smaller pixel range.
        // - The accepted mapping is approximately:
        //
        //   chromaticPixels = normalizedStrength * 10
        //
        // - The older 24px max mapping was too strong for thin HUD bars.
        // - Example: 0.173 should feel like about 1.7px, not about 4.1px.
        //
        // Important shader rule:
        //
        // - It is acceptable to use RedChannelGain / BlueChannelGain after true RT RGB sampling
        //   to make separated channels brighter.
        // - This is still true chromatic aberration because the source is the shifted RT samples.
        //
        // Accepted shader concept:
        //
        // R = sample(moduleRT, uv - offset).r * RedChannelGain
        // G = sample(moduleRT, uv).g
        // B = sample(moduleRT, uv + offset).b * BlueChannelGain
        //
        // Rejected routes, do not reopen:
        //
        // - Do not return to SkyPrisonHUDChromaticOffsetOverlayPresenter RedChannel / CyanChannel copies.
        // - Do not use per-Image Source Image None rectangle edge coloring.
        // - Do not claim that setting M_HUD_Chromatic_Auto on HpBack / HpFill is completion.
        // - Do not create a new parallel route for this module.
        //
        // Current status:
        //
        // - True chromatic route is working.
        // - Dynamic HP / LP binding is working through the bridge PlayerHUDView.
        // - Further work should only tune quality: amount, channel gain, softness, brightness,
        //   contrast, saturation and workbench slider feel.
        //
        // Future reference procedure:
        //
        // - If context is unclear, ask the user for the relevant scripts instead of inventing a new route.
        // - For this topic, ask for:
        //   SkyPrisonHUDModulePostProcessRenderer.cs
        //   SkyPrisonHUDModulePostProcess.shader
        //   SkyPrisonUIWorkbenchWindow.cs
        //   SkyPrisonPlayerHUDView.cs only if HP / LP dynamics break again.
        //
        // Absolute rule:
        //
        // - Do not open another implementation path unless the user explicitly authorizes an architecture change.
        // - Existing working chain must be preserved.


        // ============================================================
        // 14. FUTURE LINE: HUD MODULE PS/CSP-LIKE BLEND LAYER
        // ============================================================
        //
        // Original design idea:
        //
        // - First render the HUD module into RT.
        // - Apply true chromatic aberration to the module RT.
        // - Then apply a PS / CSP-like blend mode as a final layer operation.
        //
        // Status:
        //
        // - This is a valid future direction.
        // - Do not implement it during the current true chromatic aberration cleanup / quality pass.
        // - Current stable chain must remain Normal blend until the chromatic route is fully settled.
        //
        // Current stable route to preserve:
        //
        // PlayerStatusArea
        //   -> live SourceRoot UI
        //   -> SourceCamera
        //   -> Module RT
        //   -> true RGB split in ModulePostProcess shader
        //   -> Output RawImage
        //
        // Do not touch for this future line:
        //
        // - Runtime PlayerHUDView bridge
        // - HP / LP / virtual damage-lag logic
        // - Workbench -> PF -> Resources mirror sync
        // - RuntimeDriver
        // - Legacy chromatic overlay cleaner
        //
        // Reason to defer:
        //
        // - Unity UI Blend Src/Dst is not the same as PS/CSP layer compositing.
        // - Directly changing RawImage blend states can break alpha, text clarity and HUD/background relation.
        // - Full PS/CSP-like blend may require a background input / camera color sample, which is a separate system.
        //
        // Future implementation boundary:
        //
        // - Name this future task something like HUD Module Blend Layer V1.
        // - Implement it inside ModulePostProcess shader as the final stage after true chromatic RGB split.
        // - Do not create a new UI hierarchy route.
        // - Do not revive Red/Cyan overlay copies.
        // - Do not route through per-Image materials.
        //
        // Possible future shader order:
        //
        // Source UI
        //   -> Module RT
        //   -> true chromatic RGB split
        //   -> color adjustment
        //   -> optional PS/CSP-like blend recipe
        //   -> RawImage output
        //
        // Workbench rule:
        //
        // - The visible "blend mode / 合成方式" UI should be considered Legacy or Future/Advanced.
        // - For the current accepted chromatic implementation, keep it effectively Normal.
        // - If this line is reopened, first ask for:
        //   SkyPrisonHUDModulePostProcessRenderer.cs
        //   SkyPrisonHUDModulePostProcess.shader
        //   SkyPrisonUIWorkbenchWindow.cs
        //
        // Absolute rule:
        //
        // - This future blend layer must not disturb the already-working true chromatic + dynamic HUD chain.


        // ============================================================
        // 15. PLAYER STATUS AUTO VISIBILITY / 玩家状态自动显示规则
        // ============================================================
        //
        // Confirmed completed feature:
        //
        // - PlayerStatusArea supports automatic fade in / fade out based on combat state
        //   and HP / LP fullness.
        // - The feature is edited from UI Workbench inside the Player Status module layout panel,
        //   directly below the CanvasGroup block.
        // - It is part of the Player Status module's own visibility rule, not a separate combat UI page.
        //
        // Official implementation files:
        //
        // - Assets/_Project/Scripts/Editor/UIWorkbench/SkyPrisonUIWorkbenchWindow.cs
        // - Assets/_Project/Scripts/Core/Runtime/UI/HUD/SkyPrisonPlayerHUDView.cs
        // - Assets/_Project/Scripts/Core/Runtime/UI/HUD/SkyPrisonHUDCombatVisibilityController.cs
        //
        // Official save / runtime chain:
        //
        // UI Workbench setting
        //   -> saved into PF_SkyPrisonBattleHUD.prefab
        //   -> mirrored into Resources prefab / runtime delivery asset
        //   -> RuntimeDriver instantiates the mirrored PF
        //   -> SkyPrisonPlayerHUDView feeds HP / LP values
        //   -> SkyPrisonHUDCombatVisibilityController drives PlayerStatusArea CanvasGroup alpha
        //
        // Accepted behavior:
        //
        // - Non-combat + full HP + full LP: fade out.
        // - Combat state true: fade in.
        // - HP not full: keep visible if showWhenHpNotFull is true.
        // - LP not full: keep visible if showWhenLpNotFull is true.
        // - Combat exit should wait for combatExitHoldDuration before fading out.
        // - forceVisible overrides the automatic hidden state.
        // - simulatedCombatState is Workbench / preview friendly and is also useful for manual testing.
        //
        // Important alpha rule:
        //
        // - CanvasGroup alpha in layout remains the authored / base alpha.
        // - Runtime visibility alpha must multiply or be based on the authored value.
        // - Do not dirty-save a preview fade value such as 0 into the PF.
        // - Before saving PF / mirror assets, restore authored alpha through the controller when needed.
        //
        // Workbench UI rule:
        //
        // - The editable block should be labeled 自动显示规则 / Auto Visibility / Timing.
        // - Do not draw it through a disabled SerializedProperty block if the surrounding layout section is disabled.
        // - The confirmed working fix was to draw fields manually through Toggle / FloatField / Slider
        //   and write back to SkyPrisonHUDCombatVisibilityController, instead of relying only on PropertyField.
        //
        // Do not touch:
        //
        // - Do not modify HP / LP fill images individually for this behavior.
        // - Do not modify the true chromatic aberration RT / shader chain for this behavior.
        // - Do not revive the old RedChannel / CyanChannel overlay route.
        // - Do not create a second PlayerStatusArea or runtime-only fallback object.
        //
        // Future combat-state sources:
        //
        // - Enemy proximity / aggro.
        // - Player taking damage.
        // - Player dealing damage.
        // - Being locked by enemy AI.
        // - Boss battle / combat zone trigger.
        //
        // Runtime integration note:
        //
        // - Future combat systems should only call SetRuntimeCombatState(true / false)
        //   or equivalent public API on SkyPrisonHUDCombatVisibilityController.
        // - They should not directly edit PlayerStatusArea CanvasGroup.alpha.
        //
        // Current status:
        //
        // - Feature implemented and confirmed working in Workbench.
        // - Editing problem was solved in V4 / Controller V2 by avoiding disabled PropertyField drawing.


        // ============================================================
        // 16. VISION / FOG UNIT VISIBILITY / 视野与战争迷雾单位显隐
        // ============================================================
        //
        // Confirmed completed fix:
        //
        // - Expanding vision radius from Unit Definition already affected the ground fog / mask display.
        // - However, other units still looked hidden as if inside fog of war.
        // - Root cause was that ground fog visualization and unit visibility were using different final rules.
        //
        // Official involved files:
        //
        // - Assets/_Project/Scripts/Core/Runtime/Vision/SkyPrisonVisionManager.cs
        // - Assets/_Project/Scripts/Core/Runtime/Vision/SkyPrisonFogMaskRenderer.cs
        // - Assets/_Project/Scripts/Core/Runtime/Vision/SkyPrisonFogUnitVisibilityController.cs
        //
        // Chain distinction:
        //
        // Ground fog / visible terrain:
        //   SkyPrisonFogMaskRenderer
        //     -> reads PlayerFactionSources / vision radius
        //     -> writes fog mask / shader source data
        //     -> terrain becomes visibly cleared / bright
        //
        // Unit visibility:
        //   SkyPrisonFogUnitVisibilityController
        //     -> asks SkyPrisonVisionManager.ShouldHideUnitFromPlayer(runtimeBinder)
        //     -> enables / disables or fades the unit renderers
        //
        // Original broken behavior:
        //
        // - FogMaskRenderer used the expanded PlayerFactionSources radius correctly.
        // - ShouldHideUnitFromPlayer still applied an extra terrain / blocker visibility rule.
        // - Result: terrain became visible, but units inside the same visible area still looked fog-hidden.
        //
        // Accepted fix direction:
        //
        // - Unit visibility should align with the same broad player-faction vision sources used by the fog mask.
        // - For this stage, do not let an extra IsTerrainVisionBlocked / VisionBlockerRoot ray rule override
        //   unit visibility when the fog mask already says the area is visible.
        // - Priority is consistency: if the ground is visibly inside player vision, units there should recover display.
        //
        // SkyPrisonVisionManager fix note:
        //
        // - ShouldHideUnitFromPlayer was adjusted so unit visibility uses the same effective vision source logic
        //   as the fog mask: radius / faction source / soft visibility relation.
        // - Additional blocker-based hiding was removed or bypassed for this unit-fog path to avoid mismatch.
        //
        // Sampling rule:
        //
        // - Do not rely on only binder.transform.position for unit visibility.
        // - A unit may have a root point, body center, visual center, and foot position that do not perfectly match.
        // - Current accepted behavior: sample multiple meaningful points such as visual center, binder root,
        //   and near-foot/body anchor; if any important point is inside player vision, the unit can be shown.
        //
        // Do not confuse with occlusion / outline:
        //
        // - This system is about fog-of-war visibility, not 2.5D object occlusion.
        // - Do not fix fog unit visibility by touching HiddenMask RT, outline feature, proxy bodies,
        //   Spine material alpha, or PlayerStatus UI.
        // - Fog visibility and physical/visual occlusion must remain separate layers.
        //
        // Debug order for future vision problems:
        //
        // 1. Confirm Unit Definition vision radius and whether PlayerFactionSources receives it.
        // 2. Confirm FogMaskRenderer uses the updated source radius and terrain fog becomes clear.
        // 3. Confirm SkyPrisonFogUnitVisibilityController is querying the correct VisionManager.
        // 4. Confirm ShouldHideUnitFromPlayer uses the same effective player-faction source logic.
        // 5. Confirm unit renderers / Spine materials are restored after visible=true.
        // 6. Only then inspect special blockers / stealth / faction exceptions.
        //
        // Important future boundary:
        //
        // - True line-of-sight blockers for both fog mask and unit visibility can be reopened later,
        //   but then both terrain fog and unit visibility must share the same blocker-aware source.
        // - Do not reintroduce blocker checks only on units while the ground fog ignores them; that recreates
        //   the exact mismatch that was fixed here.
        //
        // Current status:
        //
        // - SkyPrisonVisionManager_UnitFogFix_V2 was tested and confirmed working.
        // - Enlarged vision now clears ground fog and correctly restores nearby unit visibility.



        // ============================================================
        // 17. UNIT GROUND SHADOW DURING JUMP / 跳跃时脚底投影锁地
        // ============================================================
        //
        // Confirmed completed fix:
        //
        // - Unit ground / stage shadow was moving upward together with the jumping unit.
        // - Desired visual rule: the character body can rise during jump, but the foot / ground shadow must
        //   remain projected onto the ground so the jump height is readable.
        //
        // Main involved file:
        //
        // - Assets/_Project/Scripts/Core/Runtime/Unit/UnitMovementController.cs
        //
        // Related but not primary:
        //
        // - TerrainGroundMotor / TerrainGroundMotorV5 controls external terrain-ground height and Rigidbody jump height.
        // - SpineAnimationDriver_Current.cs is animation state only and should not be the first place to fix shadow Y.
        //
        // Root cause:
        //
        // - The old shadow compensation logic depended mainly on UnitMovementController.jumpHeightOffset.
        // - In the current movement route, jumping is handled through ExternalTerrainMotor / TerrainGroundMotorV5
        //   and Rigidbody height, so jumpHeightOffset may stay near zero or not represent the real vertical jump.
        // - Result: ShadowRoot inherited the jumping transform height and visually floated upward with the unit.
        //
        // Accepted rule:
        //
        // - Shadow X/Z follows the unit body movement.
        // - Shadow Y stays at sampled ground height plus authored shadow offset.
        // - Jump height may affect shadow alpha / scale, but must not raise the shadow position off the ground.
        //
        // Implemented fix direction:
        //
        // - In ExternalTerrainMotor mode, calculate real jump height from the current Rigidbody/world height
        //   minus sampled ground height, instead of relying only on jumpHeightOffset.
        // - Apply inverse vertical compensation to ShadowRoot during LateUpdate / shadow update so it remains grounded.
        //
        // Conceptual formula:
        //
        //   realJumpHeight = max(0, currentBodyY - sampledGroundY)
        //   shadowPosition.y = sampledGroundY + authoredShadowYOffset
        //
        // Debug order for future shadow-jump problems:
        //
        // 1. Search UnitMovementController.cs for ShadowRoot / GroundShadow / stageShadow / jumpHeightOffset.
        // 2. Confirm whether movement is using ExternalTerrainMotor / TerrainGroundMotorV5.
        // 3. Check whether the shadow update reads real Rigidbody height and sampled ground height.
        // 4. Confirm ShadowRoot is not parented under a visual-only jumping Spine / RenderRoot node without compensation.
        // 5. Only then inspect animation scripts.
        //
        // Do not do:
        //
        // - Do not fix this by lowering the Spine visual root or weakening jump animation.
        // - Do not make shadow follow FootAnchor if FootAnchor includes jump height.
        // - Do not move the whole unit root downward to keep the shadow grounded.
        //
        // Current status:
        //
        // - UnitMovementController_GroundShadowFix_V3 was tested and confirmed working.
        // - Character jumps upward while the ground shadow remains on the ground.


        // ============================================================
        // 18. INPUT SETTINGS / INPUT PROMPT BINDING SOURCE / 输入设置与按键提示绑定源
        // ============================================================
        //
        // Confirmed input settings source files:
        //
        // - Assets/_Project/Scripts/Core/Runtime/Input/SkyPrisonInputSettings.cs
        // - Assets/_Project/Scripts/Editor/ToolWindow/SkyPrisonInputSettingsWindow.cs
        //
        // Confirmed settings asset path:
        //
        // - Assets/_Project/Data/Settings/SkyPrisonInputSettings.asset
        //
        // Confirmed editor entry:
        //
        // - Tools/按键设置
        //
        // Current input settings model:
        //
        // - SkyPrisonInputSettings is a ScriptableObject.
        // - It stores a List<SkyPrisonInputBinding> named bindings.
        // - Each binding maps one SkyPrisonInputAction to:
        //     - displayName
        //     - primaryKey
        //     - secondaryKey
        //     - gamepadKey
        //     - triggerMode
        //
        // Main query APIs:
        //
        // - GetBinding(SkyPrisonInputAction action)
        // - GetAction(SkyPrisonInputAction action)
        // - GetActionDown(SkyPrisonInputAction action)
        // - GetActionUp(SkyPrisonInputAction action)
        // - GetMoveVector()
        //
        // Current default actions / examples:
        //
        // - MoveUp / MoveDown / MoveLeft / MoveRight
        // - Sprint / Sneak
        // - Jump
        // - Dodge
        // - Interact
        // - LightAttack / HeavyAttack
        // - Skill1 / Skill2 / Skill3
        // - Inventory / Map / Menu
        //
        // Important current default behavior:
        //
        // - Input settings version is V4.
        // - Dodge is keyboard Sprint double-tap only by default.
        // - enableSprintDoubleTapDodge controls this route.
        // - Changing Sprint binding should also change the double-tap dodge key route.
        // - directDodgeKeyStillAllowed is false by default for keyboard, while gamepad direct dodge can still exist.
        //
        // Gamepad / KeyCode notes:
        //
        // - Current system uses Unity KeyCode, including KeyCode.JoystickButton0...JoystickButton19.
        // - PlayStation symbol buttons must not be used as internal enum names / keys / file names.
        // - Use semantic English internal names:
        //     - gamepad/playstation/triangle
        //     - gamepad/playstation/square
        //     - gamepad/playstation/circle
        //     - gamepad/playstation/cross
        // - Display text can be △ / □ / ○ / ×, but internal IDs and filenames should remain ASCII-safe.
        //
        // Input prompt / keycap binding rule:
        //
        // - UI prompts must bind to gameplay actions, not fixed icon sprites.
        // - Correct call style:
        //     - ShowPrompt(LightAttack)
        //     - ShowPrompt(Interact)
        //     - ShowPrompt(Dodge)
        // - Incorrect call style:
        //     - ShowIcon(Key_E)
        //     - ShowIcon(Mouse_Left)
        //
        // Required future resolver chain:
        //
        // SkyPrisonInputPromptView
        //   -> SkyPrisonInputPromptResolver
        //   -> SkyPrisonInputSettings.GetBinding(action)
        //   -> choose current active device / key slot
        //   -> convert KeyCode to iconKey
        //   -> InputPromptIconDatabase lookup
        //   -> display Sprite in CleanOverlayRoot
        //
        // Icon database role:
        //
        // - InputPromptIconDatabase should store physical key / device icons:
        //     - keyboard/e
        //     - keyboard/space
        //     - mouse/left
        //     - gamepad/xbox/a
        //     - gamepad/playstation/cross
        // - UI should never manually reference a fixed key image for gameplay actions.
        //
        // Render layer rule:
        //
        // - Input prompt / keycap icons belong to CleanOverlayRoot.
        // - They must not be captured into the HUD post-process RT.
        // - They must not participate in chromatic aberration / HUD glitch filtering.
        //
        // Future implementation caution:
        //
        // - Do not read raw SVG at runtime as prompt images.
        // - SVG may be kept as source art, but UI should display imported Sprite / Vector Sprite.
        // - When key bindings change, all prompt views should refresh from SkyPrisonInputSettings instead of storing old icon keys.
        //


        // ============================================================
        // 19. INPUT PROMPT / QUICK ITEM KEYCAP HUD CHAIN / 按键提示与快捷物品键帽链路
        // ============================================================
        //
        // Purpose:
        //
        // - Show current action bindings as keycap icons in HUD.
        // - Quick item prompts are shown under the 4 quick item slots.
        // - Prompts must follow SkyPrisonInputSettings, not fixed images.
        // - When keyboard / mouse / gamepad bindings change, prompt icons must refresh from the input settings.
        // - When a gamepad is connected, gamepad prompt icons have priority over keyboard / mouse prompts when a gamepadKey exists.
        //
        // Confirmed icon source folders:
        //
        // - Assets/_Project/UIUX/Source/Keyboard
        // - Assets/_Project/UIUX/Source/Mouse
        // - Assets/_Project/UIUX/Source/Gamepad
        //
        // Keyboard icon naming examples:
        //
        // - Key_0 ... Key_9
        // - Key_A ... Key_Z
        // - Key_Space / Key_Shift / Key_Ctrl / Key_Alt / Key_Command
        // - Key_Tab / Key_Enter / Key_Esc / Key_Delete
        // - Key_Up / Key_Down / Key_Left / Key_Right
        // - Key_F1 ... Key_F12
        // - Key_Page-Up / Key_Page-Down / Key_Home / Key_End / Key_Ins / Key_Num-lock
        // - Key_Slash / Key_Backslash / Key_+ / Key_- / Key_= / Key_Asterisk / Key_Colon
        //
        // Mouse icon naming examples:
        //
        // - Mouse_Left
        // - Mouse_Right
        // - Mouse_Scroll
        //
        // Gamepad icon naming examples:
        //
        // - Gamepad_A / Gamepad_B / Gamepad_X / Gamepad_Y
        // - Gamepad_Cross / Gamepad_Circle / Gamepad_Square / Gamepad_triangle
        // - Gamepad_Up / Gamepad_Down / Gamepad_Left / Gamepad_Right
        // - Gamepad_L / Gamepad_R
        // - Gamepad_L1 / Gamepad_R1
        // - Gamepad_L2 / Gamepad_R2
        // - Gamepad_L3 / Gamepad_R3
        // - Gamepad_Select / Gamepad_Star / Gamepad_Keycap
        //
        // Note:
        //
        // - Gamepad_triangle currently exists with a lowercase t in the asset name.
        // - Prompt database / resolver should tolerate this, but future cleanup may rename it to Gamepad_Triangle.
        //
        // Confirmed generated database:
        //
        // - Assets/_Project/UIUX/Data/InputPromptIconDatabase.asset
        //
        // Creation / update menu:
        //
        // - Tools / Sky Prison / UI / Input Prompts / 创建或更新按键图标数据库
        //
        // Core files for prompt icon binding:
        //
        // - SkyPrisonInputPromptIconDatabase.cs
        // - SkyPrisonInputPromptIconDatabaseSeeder.cs
        // - SkyPrisonInputPromptResolver.cs
        //
        // Database role:
        //
        // - Maps stable icon keys to Sprite assets.
        // - Examples:
        //     - keyboard/1 -> Key_1 Sprite
        //     - keyboard/space -> Key_Space Sprite
        //     - mouse/left -> Mouse_Left Sprite
        //     - gamepad/xbox/a -> Gamepad_A Sprite
        //     - gamepad/playstation/cross -> Gamepad_Cross Sprite
        //
        // Resolver role:
        //
        // - Input action -> SkyPrisonInputSettings.GetBinding(action)
        // - Choose display key:
        //     - If gamepad is connected and binding.gamepadKey is valid, use gamepadKey.
        //     - Otherwise use primaryKey.
        //     - If primaryKey is empty, use secondaryKey.
        // - KeyCode -> iconKey -> InputPromptIconDatabase -> Sprite.
        //
        // Quick item action expansion:
        //
        // - SkyPrisonInputDefinitions.cs was expanded to include:
        //     - QuickItem1
        //     - QuickItem2
        //     - QuickItem3
        //     - QuickItem4
        //
        // Quick item default keyboard keys:
        //
        // - QuickItem1 -> Alpha1
        // - QuickItem2 -> Alpha2
        // - QuickItem3 -> Alpha3
        // - QuickItem4 -> Alpha4
        //
        // Quick item prompt component:
        //
        // - SkyPrisonQuickItemPromptStrip.cs
        //
        // Component responsibilities:
        //
        // - Owns Show Key Prompts toggle.
        // - Holds Quick Slots Root reference.
        // - Holds Slot Rects[0..3] references.
        // - Holds Input Settings and Input Prompt Icon Database references.
        // - Polls gamepad connection state.
        // - Resolves QuickItem1~4 prompt icons.
        // - Generates QuickItemPrompt_01~04 Images.
        // - Keeps Image.material = null / Default UI material.
        // - Does not DestroyImmediate prompt objects during Workbench / ExecuteAlways refresh.
        //
        // Correct runtime hierarchy for quick item prompts:
        //
        // SafeAreaRoot
        //   -> HUDRoot
        //      -> QuickSlotsArea
        //         -> __ModulePostProcessOutput_V87
        //         -> __SkyPrisonHUDModuleRTSource_V87_QuickSlotsArea
        //         -> QuickItemPromptOverlay
        //            -> QuickItemPrompt_01
        //            -> QuickItemPrompt_02
        //            -> QuickItemPrompt_03
        //            -> QuickItemPrompt_04
        //
        // Important hierarchy rule:
        //
        // - QuickItemPromptOverlay must use QuickSlotsArea module coordinates.
        // - It must stay on the display side of QuickSlotsArea.
        // - It must not be moved into the RT SourceRoot.
        // - It must not be placed under global ScreenSpaceOverlay / CleanOverlayRoot if that breaks module coordinates.
        //
        // Module post-process renderer responsibilities:
        //
        // - SkyPrisonHUDModulePostProcessRenderer.cs must treat QuickItemPromptOverlay / QuickItemPrompt_01~04 as display-side clean prompt nodes.
        // - It must not move them into __SkyPrisonHUDModuleRTSource_V87_*.
        // - It must not hide them as stray display graphics.
        // - It must keep the overlay above __ModulePostProcessOutput_V87.
        // - If stale QuickItemPrompt nodes exist inside RT SourceRoot, they should be disabled / renamed, not DestroyImmediate'd during sensitive editor/runtime timing.
        //
        // Workbench integration:
        //
        // - In UI Workbench, when current module is QuickSlotsArea / 快捷栏, the Layout inspector exposes:
        //     - 显示快捷键提示
        //     - 刷新快捷键提示
        //
        // Workbench generation rule:
        //
        // - Workbench should bind Slot_01~04 references explicitly into SkyPrisonQuickItemPromptStrip.slotRects.
        // - Do not make PromptStrip guess slots through global recursive scene search.
        // - Do not bind to world/character UI by fallback.
        //
        // Confirmed final preview distinction:
        //
        // - Runtime generated HUD was already fixed before the final Workbench preview cleanup.
        // - The remaining problem was only the Workbench simulated preview showing chromatic aberration on key prompts.
        // - Correct fix for that case is Workbench-only:
        //     - During Workbench RT preview, temporarily hide QuickItemPromptOverlay from the post-processed preview pass.
        //     - After the post-processed preview is drawn, draw key prompts again as a clean Editor GUI overlay.
        //     - This preview overlay must not be written into PF.
        //     - This preview overlay must not alter runtime HUD hierarchy.
        //
        // Critical lesson:
        //
        // - If runtime generated HUD is correct, do not keep changing runtime hierarchy, Renderer, Canvas, SourceRoot, or Resources mirror just to fix Workbench preview artifacts.
        // - Workbench preview artifacts should be fixed in SkyPrisonUIWorkbenchWindow preview drawing layer.
        // - Actual runtime chain and Workbench simulated preview chain must be treated as separate layers.
        //
        // Current clean Workbench preview solution:
        //
        // - SkyPrisonUIWorkbenchWindow_V21_WorkbenchCleanPromptPreview
        // - It only changes Workbench preview behavior:
        //     - hides prompts during RT preview draw
        //     - redraws prompts cleanly on top with Editor GUI
        //     - does not change PF / Resources / runtime Renderer / PromptStrip
        //
        // Do not repeat the failed detours:
        //
        // - Do not solve Workbench-only chromatic preview artifacts by moving prompts to ScreenSpaceOverlay.
        // - Do not solve it by changing SafeAreaRoot / CleanOverlayRoot hierarchy.
        // - Do not solve it by making Renderer destroy prompt nodes during ExecuteAlways / serialization.
        // - Do not use DestroyImmediate for prompt cleanup during Workbench refresh, Prefab save, Canvas rebuild, or serialization.
        // - Do not use world/screen coordinates when module-local coordinates are required.
        //


        // -----------------------------------------------------------------------------------------
        // 20. BUILD PLAYER VISUAL Y OFFSET / UNIT ROOT FOOTPOINT CONVENTION
        //     Build 角色视觉高度偏移与 UnitRoot 脚底约定
        // -----------------------------------------------------------------------------------------
        // Final confirmed problem:
        //
        // - Build character visual Y offset was NOT caused by VisualRoot / SpineRoot localPosition.
        // - Editor Play diagnostic showed:
        //     - Root Y = 0.0200
        //     - VisualRoot local = (0,0,0)
        //     - SpineRoot local = (0,0,0)
        //     - Capsule center.y = 2.1086
        //     - Capsule height = 4.2172
        //     - Capsule bounds.min.y = 0.0200
        //
        // - Build diagnostic showed:
        //     - Root Y = 1.3200
        //     - VisualRoot local = (0,0,0)
        //     - SpineRoot local = (0,0,0)
        //     - Capsule center.y = 0.7000
        //     - Capsule height = 4.0000
        //     - Capsule bounds.min.y = 0.0200
        //
        // Root cause:
        //
        // - The project convention is:
        //     - UnitRoot / Player Root represents the foot / ground point.
        //     - VisualRoot.localPosition should normally remain authored at zero.
        //     - SpineRoot.localPosition should normally remain authored at zero.
        //
        // - In Build, CapsuleCollider.center.y was too low for the capsule height.
        // - TerrainGroundMotorV5 correctly kept CapsuleCollider.bounds.min.y on ground.
        // - Because the capsule bottom was below UnitRoot, TerrainGroundMotorV5 raised the entire Player Root.
        // - VisualRoot and SpineRoot were not independently wrong; they followed the raised UnitRoot.
        //
        // Important conclusion:
        //
        // - Do NOT fix this with visual offset compensation.
        // - Do NOT use Renderer bounds to guess the foot point.
        // - Do NOT add a keeper component that restores a second authored offset source.
        // - Do NOT patch VisualRoot / SpineRoot Y to cancel the symptom.
        //
        // Correct solution:
        //
        // - Fix the collision capsule root convention in UnitDefinitionRuntimeApplier.
        // - For character units whose UnitRoot is the foot point:
        //     - CapsuleCollider.center.y must be height * 0.5 + bottomPadding.
        //     - Capsule bottom must align to UnitRoot / ground origin.
        //
        // Final fix file:
        //
        // - UnitDefinitionRuntimeApplier_CapsuleRootConvention_V37.cs
        //
        // Behavior of V37:
        //
        // - When applying character collision settings, it preserves the project root convention:
        //     - UnitRoot = foot / ground point
        //     - Capsule bottom = UnitRoot
        // - Even if UnitDefinition collisionLocalCenter contains an old value such as y=0.7,
        //   runtime applies the corrected center based on capsule height.
        //
        // Expected stable Build result:
        //
        // - Capsule center.y becomes approximately height * 0.5.
        // - TerrainGroundMotorV5 no longer needs to raise Player Root to keep capsule bottom grounded.
        // - Root Y stays consistent with Editor Play.
        // - VisualRoot.localPosition remains (0,0,0).
        // - SpineRoot.localPosition remains (0,0,0).
        // - Editor Play and Build display match through the same authoritative chain.
        //
        // Diagnostic chain used to prove this:
        //
        // - SkyPrisonUnitVisualBuildProbe.cs
        // - SkyPrisonUnitVisualBuildProbeEditor.cs
        // - Later fixed as:
        //     - SkyPrisonUnitVisualBuildProbe_V4_PathFixed.cs
        //
        // Probe purpose:
        //
        // - Read-only comparison between Editor Play and Build.
        // - Logs Player path, UnitDefinition, VisualRoot, SpineRoot, Skeleton transform,
        //   CollisionRoot, CapsuleCollider center/height/bounds, TerrainGroundMotorV5 state,
        //   and SpineAnimationDriver base local position.
        // - Build output path:
        //     - Application.persistentDataPath/SkyPrisonUnitVisualBuildProbe.log
        //
        // Debugging lesson:
        //
        // - If Build and Editor Play differ, first compare the runtime data chain.
        // - Do not assume the visible symptom is the broken node.
        // - In this case, the visible symptom was character visual height,
        //   but the actual broken source was CapsuleCollider.center under the UnitRoot footpoint convention.
        //
        // Chain rule for future unit visual / collision issues:
        //
        // - Authority / PlayerAuthority selects an existing Player; it does not spawn the Player.
        // - Player exists in the Tower scene.
        // - UnitDefinitionRuntimeBinder / UnitDefinitionRuntimeApplier applies definition data.
        // - TerrainGroundMotorV5 grounds the physical root via CapsuleCollider bounds.
        // - SpineAnimationDriver_Current records and preserves SpineRoot local base transform.
        // - Therefore, first inspect collision/root convention before changing VisualRoot / SpineRoot.
        //


        // ============================================================
        // 21. INVENTORY SYSTEM / 背包系统
        // ============================================================
        //
        // Script folder:
        //
        // Assets/_Project/Scripts/Core/Inventory/
        //
        // Core files:
        //
        // - InventoryItemEntry.cs       — 运行时条目（definition + count + acquireTime）
        // - InventoryRuntime.cs         — 背包容器 MonoBehaviour（增删/堆叠/排序/筛选）
        // - InventoryRuntimeBootstrap.cs — 挂在 PlayerRoot，单例入口
        //
        // ItemDefinition fields used by inventory:
        //
        // - maxStackCount  — 单格堆叠上限，=1 时不显示数量，禁用拆分
        // - canDiscard     — 是否可丢弃
        // - isKeyItem      — 重要物品，OnValidate 强制 canDiscard=false
        // - weightOrQuartz — 用于重量排序
        // - category / majorCategory — 用于筛选Tab
        //
        // UI PF:
        //
        // - Assets/_Project/Prefabs/UI/Window/PF_SkyPrisonInventory.prefab
        // - Resources mirror: Assets/Resources/UI/Window/PF_SkyPrisonInventory.prefab
        // - metadata.uiId = "inventory", kind = Window, defaultVisible = false
        // - blocksRaycasts = true, closeOnEscape = true, sortingOrder = 1100
        //
        // Input rule when inventory is open:
        //
        // - lockGameplayInput = true → UnitActionController 屏蔽攻击/闪避输入
        // - 移动输入保留
        // - 屏蔽的是"行为"而不是"按键"，改键自动兼容
        //
        // Slot interaction rules:
        //
        // - 拖拽同种物品到有空间的格子 → 合并，超出堆叠上限留在原格
        // - 右键格子 → 拆分弹窗（输入数量，1 到 count-1）
        // - 拖出背包边界 → 丢弃弹窗（确认 + 数量选择），canDiscard=false 直接拦截
        // - 格子数量达到 maxStackCount → 右下角数量字体变橙色
        //
        // Filter tabs:
        //
        // 全部 | 消耗品 | 材料 | 装备 | 任务 | 重要
        //
        // Sort fields:
        //
        // 获取时间 / 重量 / 类别 / 名称，配合升降序切换按钮
        //
        // Key prompt rule:
        //
        // - 背包内按键提示必须在 display side，不进色差 RT
        // - 与 QuickItemPromptOverlay 相同规则（见脚注 19）
        //
        // Capacity expansion:
        //
        // - 初始 20 格，科技树解锁调用 InventoryRuntime.ExpandCapacity(n)
        //
        // Discard popup:
        //
        // - 归入 SkyPrisonUIPrefabKindV1.Popup 类别
        // - 需要单独 PF，通过 SkyPrisonWindowManager 打开
        //
        // Window open/close wiring:
        //
        // - 开启: SkyPrisonWindowManager_V1.Open(inventoryPrefab)
        // - 关闭: SkyPrisonWindowManager_V1.Close("inventory")
        // - 状态查询: SkyPrisonWindowManager_V1.IsOpen("inventory")
        // - ToggleInventory() 在 SkyPrisonPlayerInputRouter 里使用 IsOpen() 判断，
        //   不再用 _inventoryOpen bool，避免 X 按钮关闭后状态失步
        //
        // 窗口 vs 战斗 HUD 层级(已定: 打开窗口时隐藏 HUD):
        //
        // - 问题: 快捷键帽提示是 overrideSorting + sortingOrder=32000 的嵌套 Canvas(为压过色差RT保持清晰)，
        //   窗口只有 1100，ScreenSpaceOverlay 全局按 sortingOrder 排 → 键帽/HP/LP 盖在窗口上。
        // - 方案(不动那条敏感的键帽/HUD链): WindowManager 在有 Window/Popup 打开时，
        //   把战斗 HUD 根的 CanvasGroup 淡出(alpha 连嵌套 Canvas 一起作用)，全部关闭后淡回。
        //   HUD 实例经 SkyPrisonRuntimeUIDriver.HudInstance 取，UpdateHudForWindows() 在 Open/Close 调。
        // - 没走"把窗口排到 32000 之上": sortingOrder 上限 32767，逼近上限脆弱，且 HUD 仍可见会更乱。
        //
        // 菜单态输入屏蔽(全局闸):
        //
        // - UI 是 ScreenSpaceOverlay，但攻击/闪避走 legacy Input 轮询(settings.GetActionDown)，
        //   不会被 UI 吃掉 → 点背包会"漏"到世界触发攻击/闪避。必须显式屏蔽。
        // - 铁则: 有 Window/Popup 打开时 SkyPrisonWindowManager_V1.AnyBlockingWindowOpen=true。
        //   任何读鼠标/世界点击的玩法系统都查它, 窗口打开时屏蔽自身功能(移动/跳跃保留)。
        // - SkyPrisonPlayerInputRouter 主路径+兜底路径都用此静态闸拦 攻击(左/右键)/闪避(双击+直接键)。
        //   UnitActionController 不自己轮询输入(只收 RequestX), 故拦在路由即可。拾取控制器也查 HasAnyWindowOpen。
        // - 以后地图/世界新增左右键功能, 直接 if (SkyPrisonWindowManager_V1.AnyBlockingWindowOpen) return; 即可。
        //
        // InventoryWindowController (Core/Inventory/InventoryWindowController.cs):
        //
        // - 挂在 PF_SkyPrisonInventory 根节点（由 workbench builder 自动添加）
        // - Awake 里 FindObjectOfType<SkyPrisonWindowManager_V1>() 获取 manager
        // - 关闭按钮 onClick → CloseWindow() → windowManager.Close("inventory")
        // - Init(Button btn) 供 builder 在创建 PF 时注入 closeButton 引用
        //
        // 格子渲染层 (数据→视图桥, 已实现):
        //
        // - InventorySlotView.cs   — 单格视图。持有 Icon/CountText/NewBadge/CellBg 引用，
        //     Bind(entry)/SetEmpty()/SetDimmed(bool)。自己不碰数据层。容量外整格由 GridView SetActive(false)。
        //     builder 构建 PF 时 AddComponent 并 Init(...) 注入引用。
        // - InventoryGridView.cs   — 挂 panel 上的网格绑定器。订阅 InventoryRuntime.OnInventoryChanged，
        //     Refresh() 把条目按打包顺序映射到格子: i>=Capacity 锁定, i<Slots.Count 绑定, 其余空格。
        //     ApplyFilter(tab) 把不匹配物品调暗(下放自 controller)。SetGridContent() 由 builder 注入。
        // - 容量模型(已定): builder 建满 40 格(5×8), 当前容量(初始20)内可用; 容量外的格子
        //     Refresh 里直接 SetActive(false) 隐藏——不显示锁定态, 留扩充悬念。
        //     同时 Refresh 把 GridContent 高度收到正好容纳当前容量行数(20格=4行=650px),
        //     使 ScrollRect 滑不出下面不存在的格子。视口也正好 4 行(见窗口高度 914 的布局注释)。
        //     科技树 ExpandCapacity → OnInventoryChanged → Refresh 自动多露格 + 撑高内容。
        //     不在运行时实例化格子(PF 里建满, 多余的隐藏)。
        // - 图标在 builder 里建(所见即所得): 每格加 Icon Image(默认隐藏) + SlotView。无 LockOverlay。
        // - 格子底图(cellBg) 走全局样式 SkyPrisonUIGlobalStyleSettings_V1:
        //     inventorySlotSprite(留空=内置圆角矩形) / inventorySlotColor / inventorySlotSliced。
        //     Workbench「全局样式」面板里编辑, 改后必须「恢复背包默认结构...」重建 PF 才生效(build 时写入)。
        // - 窗口皮肤(同一全局样式, 按类别共享一张图换整窗): builder 用 SkinPlate() 应用,
        //     windowPanelSprite(面板底图) / windowBarSprite(标题/筛选/排序/负重栏共用) /
        //     windowButtonSprite(关闭/升降序/整理/排序下拉共用) / windowCloseIconSprite(替换×文字)。
        //     留空处保持内置纯色; 有 sprite 用九宫格 Sliced+原色作 tint。同样 build 时写入, 改后重建 PF。
        //     美术建议: UIUX/Window/Styles/Default/Sprites/ 下 UIWindow_Default_*.png, 2× 尺寸, 九宫格设 Border。
        //     以后某按钮要单独不同图, 再加 per-element override 字段。
        // - 默认线框风(无皮肤图时, 工业/BLAME 味, 替代深灰实心块): builder 用图元辅助画——
        //     AddLine/AddBottomDivider/AddTopDivider/AddOutline/AddCornerBrackets, StyleBar/StyleButton。
        //     横条=透明不加深+白色分割线(标题/筛选/排序底分割, 负重顶分割); 按钮=透明+白色细线外框; 面板=白色四角角标。
        //     调色板 uiLine=uiAccent=纯白实心(Color.white)。模块靠白线区分, 不加深背景(用户要求)。白线必须 alpha=1 实心。
        //     有对应皮肤 sprite 时走 sprite, 不画线框。改皮肤/格子字段时 Workbench 自动重建预览(delayCall)。
        // - controller.ApplyFilter 现在只委托 _gridView.ApplyFilter; 旧的 Slot_N Graphic 遍历已删除。
        // - 改了格子结构后必须用 "恢复背包默认结构..." 重建 PF + 同步 Resources 镜像才生效。
        //
        // 真实数据层 + 拾取(已实现, 常驻单例方案):
        //
        // - 背包数据层由 SkyPrisonRuntimeSystemsBootstrapper 创建为常驻运行时单例:
        //     EnsureChildComponent<InventoryRuntimeBootstrap>(SkyPrisonInventory_Runtime)。
        //     不再依赖玩家 prefab; InventoryRuntimeBootstrap.Instance.Inventory 全局可达。
        // - BUG 修复: InventoryRuntimeBootstrap 以前 initialCapacity/maxWeight 字段从不生效。
        //     现在 Awake 里 Inventory.SetInitialCapacity(20) + SetMaxWeightCapacity(50)。
        // - 拾取: SkyPrisonItemPickupController(常驻单例, 同启动器创建 SkyPrisonItemPickup_Runtime)。
        //     每帧在 SkyPrisonPlayerAuthority.CurrentPlayerUnit 周围 pickupRadius 内找最近
        //     LootDropWorldObject(用其静态 Active 注册表, 不每帧 FindObjectsOfType), 显示世界提示;
        //     按 Interact 键 → inv.AddItem(loot.Item, loot.Count), leftover>0 则 SetRemaining 留地上。
        //     背包窗口打开时不拾取。提示是简版 Text([key] 拾取 名称), 后续可换键帽图标(脚注18/19)。
        // - LootDropWorldObject 加了 static Active 注册表 + Item(类型化) + SetRemaining()。
        // - 闭环现状: 掉落物 → Interact 拾取 → AddItem → GridView 刷新显示图标/数量 → 负重条数值动。
        // - 测试: 场景放一个挂 LootDropWorldObject 的物体并指定 ItemDefinition, 走近按 E。
        //
        // 格子操作交互(已实现):
        //
        // - 数据模型是「打包列表」(配合整理/排序按钮, 物品始终靠前不留空洞)，所以拖拽语义=合并/重排,
        //   不是自由落格。InventoryRuntime 新增 MoveSlot(from,to) 做重排(remove+insert)。
        // - InventorySlotInteractor(每格): IBeginDrag/IDrag/IEndDrag/IPointerClick(右键)，
        //   只转事件给面板级 SkyPrisonInventoryInteraction，SlotIndex 取自同物体 InventorySlotView。
        // - SkyPrisonInventoryInteraction(挂 panel): 拖拽幽灵(独立 Canvas sortingOrder 1200, raycastTarget=false)、
        //   落点用 EventSystem.RaycastAll 找目标格(不依赖 OnDrop 顺序)、
        //   同种且有空间→MergeSlots，否则→MoveSlot，拖出面板矩形→丢弃；右键→拆分。
        //   拆分/丢弃共用一个运行时数量弹窗(全面板遮罩+小框+/-/确认/取消)。改完数据 GridView 自动刷新。
        // - 拆分前置: maxStackCount>1 且 count>1 且有空格; 丢弃前置: entry.CanDiscard(重要物品拦截)。
        // - 已知取舍: 格子消费拖拽事件 → 拖格子时 ScrollRect 不滚动(滚轮仍可); 空格拖拽不滚动。
        //
        // 底部操作提示条(已实现, 全局通用):
        //
        // - SkyPrisonWindowHintBar(自带 ScreenSpaceOverlay Canvas, sortingOrder 1150): 屏幕底部居中、
        //   HorizontalLayoutGroup+ContentSizeFitter 按内容自动撑宽(抗分辨率不重叠)。所有窗口共用、同规格(字号20)。
        // - SkyPrisonWindowManager_V1 在 Open/Close(UpdateHudForWindows 内 UpdateHintsForWindows)收集所有打开
        //   窗口的 ISkyPrisonWindowHintProvider.GetWindowHints()，交给提示条 Show/Clear。HUD 隐藏它仍可见。
        // - SkyPrisonWindowHint: 动作型 Action(用当前绑定键的键帽图标, live) / 字面型 Icon(直接给 iconKey
        //   如 mouse/left、mouse/right, 带文字兜底)。chip = [键帽图标 或 [token]] + 描述, 无图标退文字。
        //   InventoryWindowController: 拖拽(mouse/left)换位合并 | 右键(mouse/right)拆分 | 拖出(mouse/left)丢弃 | B键 关闭。
        // - 键帽数据库运行时获取: 借用 SkyPrisonQuickItemPromptStrip.RuntimeDatabase / RuntimeInputSettings
        //   (strip OnEnable 缓存, HUD 隐藏仍在)。数据库不在 Resources, 这是运行时唯一可达路径。
        //   图标走 SkyPrisonInputPromptResolver.TryResolveActionIcon(同战斗 HUD)。
        // - 设备家族单一化(铁则): Show 里只决定一次 GetPreferredDeviceStyle(有手柄→手柄, 否则键鼠);
        //   手柄模式下无手柄绑定的动作/无 gamepadIconKey 的手势 → 隐藏(continue), 不与手柄图标混排;
        //   键鼠模式不显示手柄图标。鼠标左右键+键盘是同一"键鼠"家族, 同时出现不算混合。
        //   注: 拖拽/右键手势的图标是 mouse/left、mouse/right(Mouse_Left/Right sprite), 若看着像手柄是 seed 的图错。
        // - 背景: 横向渐变淡出 sprite(两端 alpha→0)+ 低 alpha(0.55), 运行时 BuildFadeSprite 生成。
        // - 残影修复(换边躲避): SkyPrisonInventoryChromatic 用 ScreenCapture 截全屏, 会把提示条(overlay 1150)
        //   截进快照 → 窗口盖住它时显示偏移副本=残影。最终方案: 不隐藏, 而是让提示条躲开窗口——
        //   Chromatic.LateUpdate 调 DriveHintBarEdge(): 面板盖住提示条「固定底部矩形」(TryGetBottomScreenRect)
        //   时 SetEdgeTop(true) 移到顶部, 拖开移回底部; TearDown 复位底部。
        //   关键: 重叠判定必须用「固定底部矩形」而非提示条当前矩形, 否则跳顶后判定翻转会来回抖。
        //   提示条移到顶部后不在面板区域内 → 既无残影也无闪烁, 且提示始终可见(优于隐藏方案)。
        // - 背包键默认改 B 主 / Tab 副(SkyPrisonInputSettings)。
        // - 待办: 负重接 UnitLoadRuntime、NEW 徽章触发、快捷栏/消耗。
        //
        // Scene setup required:
        //
        // - SkyPrisonPlayerInputRouter 组件：Inspector 连好 windowManager 和 inventoryPrefab
        // - 用 UI Workbench "恢复背包默认结构..." 刷新 PF 以获得 CloseButton + Controller
        //
        // Do not:
        //
        // - 不要在 InventoryRuntime 里直接操作 UI
        // - 不要绕过 CanDiscard / isKeyItem 检查直接修改 count
        // - 不要把获取时间存到 ItemDefinition，acquireTime 只属于运行时条目
        //
        // Filter tab 选中辉光 / 选中标签发光:
        //
        // - 需求: 选中标签文字背后有柔和蓝光，光晕长短跟文字实际宽窄匹配，
        //   多语言动态文字也要自适应，不能烤死贴图。不要"沿字形描边"那种僵硬效果。
        // - 走过的死路:
        //     1. TMP Glow keyword → 沿 SDF 边界贴边，本质是描边，一眼僵硬。
        //     2. TMP Underlay(单层) → 扩散范围被字体图集 SDF padding 卡死，
        //        dilate 小=贴字细，dilate 大=蓝色实心 band 被推出且末端硬边，还是描边。
        //     3. Underlay + 放大透明副本 → 仍受 padding 限制，效果不对。
        // - 最终方案(商业级 #3 局部 Bloom 思路): SkyPrisonTabGlowRenderer.cs
        //     离屏隐藏相机(专用 layer 31 + 远处 RigOrigin)把选中标签文字渲到透明 RT
        //     → 降采样/升采样模糊 → 染 glowColor → RawImage 垫在清晰白字背后。
        //     光形状由文字本身决定，长短/多语言自动匹配。1:1 渲染居中对齐。
        //     只在 SelectTab / 语言切换时重渲一次，相机 enabled=false 手动 Render，不每帧。
        // - InventoryWindowController 里: 清晰字层始终用干净字体材质只换颜色(锐利可读)，
        //   辉光完全交给 _glowRenderer.ShowFor(selectedLabel)，单张显示层自动移动到当前标签。
        // - 可调: glowColor / glowIntensity / glowPadding(光晕外渗像素余量) / LayerCount(常量)。
        // - 为什么不直接全局 Bloom: 背包是 ScreenSpaceOverlay，URP 之后才合成，
        //   后处理碰不到(同色收差脚注 04/12/13 的老墙)。所以走局部 RT 模糊而非全局 Bloom。
        //
        // ★ RT 模糊辉光的四个通用坑(已全部踩平，下次做任何 UI 光晕/模糊直接复用):
        //     1. 对齐: 显示层要对齐到「标签自身 rect 中心」(算 pivot→center 偏移)，
        //        不能假设 label.parent 是单个格子——TMP 常直接挂在 FilterTab 上，
        //        父级是整条 FilterBar，按父级居中会跑到整条栏正中。
        //     2. 布局: FilterBar 挂 HorizontalLayoutGroup 会把显示层当排版元素挤变形/压成
        //        零尺寸而消失。显示层必须加 LayoutElement.ignoreLayout=true。
        //     3. 黑边: 离屏相机背景要清成「透明白」(RGB=1,A=0) 不是透明黑。模糊不分
        //        颜色和透明度，透明区 RGB=黑会渗进光晕半透明边缘 → 发黑光圈。
        //        清成白后 RGB 恒为白，颜色全由显示层蓝色决定，边缘只在 alpha 上渐隐。
        //     4. 盖字: 显示层插到选中标签紧前面(背景之上、文字之下)。刷新时光晕已在
        //        标签前面，直接 SetSiblingIndex(labelIdx) 会多跳一格盖住文字。
        //        正解: 先 SetAsLastSibling() 让标签索引稳定，再 SetSiblingIndex(labelIdx)。
        //     5. 太弱: 文字模糊摊开后每像素 alpha 很低，单层受 alpha≤1 天花板压制提不亮。
        //        用多层(LayerCount=3)同一张 RT 叠加，alpha 累积 = 等效 additive 提亮。


        // ============================================================
        // 22. LOCALIZATION SYSTEM / 多语言系统
        // ============================================================
        //
        // 语言配置资产:
        //
        // Assets/_Project/Data/ProjectSettings/LocalizationProjectSettings.asset
        //
        // 配置类:
        //
        // - LocalizationProjectSettings (ScriptableObject)
        //     languages: List<LanguageEntry>
        //       - languageCode: "zh-CN" / "ja-JP" / "en-US"
        //       - displayName: 编辑器显示名
        //       - isDefault: 主语言标记
        //       - enabled: 是否启用
        //       - primaryFont / fallbackFonts
        //
        // 文本存储结构:
        //
        // - LocalizedTextEntry { languageCode, text }
        // - 每个需要多语言的字段存 List<LocalizedTextEntry>
        // - 示例: ItemDefinition.localizedNames, localizedDescriptions
        //         UnitDefinition.localizedNames
        //         StatusDefinition 等
        //
        // Editor 侧取文本 (LocalizationSettingsUtility):
        //
        // - LocalizationSettingsUtility.GetOrCreateSettings() 加载配置资产
        // - GetLocalizedText(List<LocalizedTextEntry>, languageCode) 遍历列表取值
        // - 编辑器 Inspector 动态按 settings.languages 展开输入框，有几种语言显示几行
        // - 不写死语言数量，新增语言只需在 LocalizationProjectSettings 里加一条
        //
        // 运行时取文本 (当前状态):
        //
        // - 目前没有运行时 LocalizationManager
        // - 运行时多语言查询层尚未建立
        // - 需要建立前需确认: 运行时当前语言存在哪里（PlayerPrefs / 存档 / 启动参数）
        //
        // UI 静态文本多语言 (待实现):
        //
        // - 需要一个 UILocalizationTable (ScriptableObject)
        //     entries: List<UILocalizationEntry>
        //       - key: "ui_inventory_title" / "ui_filter_all" 等
        //       - texts: List<LocalizedTextEntry>
        // - 编辑器同样按 LocalizationProjectSettings 动态展开语言行
        // - 运行时通过 key + 当前语言 code 查询
        //
        // 运行时系统文件 (已实现):
        //
        // - Core/Localization/LocalizationRuntime.cs
        //     MonoBehaviour 单例，挂在常驻对象上
        //     持有 LocalizationProjectSettings 引用
        //     当前语言存 PlayerPrefs "SkyPrison.Language"
        //     GetText(List<LocalizedTextEntry>, fallback) — 按当前语言取值，找不到退回默认语言
        //     Resolve(entries, code, fallback) — 静态版本，不依赖单例
        //
        // - Core/Localization/UILocalizationTable.cs
        //     ScriptableObject，存所有 UI 静态文字
        //     entries: List<UILocalizationEntry> { key, List<LocalizedTextEntry> }
        //     Get(key, fallback) — 运行时查询
        //     GetForCode(key, code, fallback) — 编辑器/静态查询
        //     EnsureEntry(key, codes) — 编辑器同步语言列表
        //     StandardUIKeys — 预置背包/筛选/排序/丢弃等 UI key 列表
        //
        // - Editor/SkyPrisonEditor/Pages/Localization/UILocalizationTableEditor.cs
        //     CustomEditor，动态按 LocalizationProjectSettings 展开语言输入行
        //     "同步标准 UI Key" 按钮 — 一键生成 StandardUIKeys 所有条目
        //     "同步语言列表" 按钮 — 新增语言后补全所有条目
        //
        // 创建资产:
        //
        // 右键 Project → Create/Sky Prison/Localization/UI Localization Table
        // 建议存放: Assets/_Project/Data/Localization/UILocalizationTable.asset
        //
        // 编辑器原则:
        //
        // - 所有多语言编辑入口必须从 LocalizationProjectSettings 读取语言列表
        // - 禁止在编辑器代码里硬编码 "zh-CN" / "ja-JP" / "en-US" 语言数量
        // - 新增语言后，编辑器所有多语言输入区域自动出现新语言输入框

        // ============================================================
        // 23. UI RUNTIME BOOTSTRAP / UI 运行时启动结构
        // ============================================================
        //
        // 核心文件:
        //
        // Core/Runtime/UI/Core/SkyPrisonRuntimeUIDriver.cs   ← 真正在用的
        // UI/Core/SkyPrisonUIRuntimeDriver_V1.cs             ← 旧版，场景里未使用
        //
        // 职责 (SkyPrisonRuntimeUIDriver):
        //
        // - Awake/Start 调用 EnsureUIRoot() → 创建/找到 SkyPrisonRuntimeUIRoot Canvas
        // - EnsureBattleHUDInstance() → 从 Resources 或序列化字段加载 PF，实例化 BattleHUD
        // - BattleHUD 有自己的 Canvas → parent=null → 与 SkyPrisonRuntimeUIRoot 是兄弟节点
        // - EnsureUIRoot() 末尾 AddComponent<SkyPrisonWindowManager_V1> 到 UIRoot 上
        // - Boot Coordinator 集成：EnsureHudReadyForBoot / SetPresentationEffectsEnabled
        //
        // 真正的入口: SkyPrisonRuntimeSystemsBootstrapper
        //
        // - [RuntimeInitializeOnLoadMethod] 任何场景加载后自动运行
        // - EnsureRuntimeSystems() 在 SkyPrisonRuntimeSystems 下创建各子系统
        // - 新增系统只需在这里加一行 EnsureChildComponent<T>
        //
        // 结果层级:
        //
        // SkyPrisonRuntimeSystems
        //   SkyPrisonRuntimeBootCoordinator_Runtime
        //   SkyPrisonPlayerAuthorityBootstrap_Runtime
        //   SkyPrisonRuntimeUIDriver_Runtime   ← 创建 UIRoot + BattleHUD
        //   SkyPrisonWindowManager_Runtime     ← 在此统一管理，FindObjectOfType 可找到
        //
        // SkyPrisonRuntimeUIRoot (Canvas)  ← UIDriver 创建
        // SkyPrisonBattleHUD_Runtime       ← BattleHUD 自带 Canvas，兄弟节点
        //   SafeAreaRoot
        //     HUDRoot
        //       PlayerStatusArea / QuickSlotsArea / ...
        //   DontDestroyOnLoad             ← Unity 系统节点
        //
        // WindowManager 获取方式:
        //
        // - PlayerInputRouter.ResolveReferences() 调用 FindObjectOfType<SkyPrisonWindowManager_V1>()
        // - 首帧 Awake 顺序保证 Driver 先于 InputRouter 创建好 WindowManager
        //
        // 注意:
        //
        // - 场景里不要手动放 SkyPrisonWindowManager_V1，由 Driver 自动创建
        // - 场景里不要手动放 BattleHUD PF 实例，同样由 Driver 控制
        // - 需要窗口打开/关闭的系统均通过 FindObjectOfType 或事件获取 WindowManager

        // ============================================================
        // 死亡复活弹窗 PlayerDeathReviveUI — 设计规则备忘
        // ============================================================
        //
        // 文件:
        //   UI/HUD/PlayerDeathReviveUI.cs              — 运行时逻辑
        //   Editor/.../Pages/UI/CreatePlayerDeathRevivePrefab.cs — prefab builder
        //   Core/Logic/PlayerDeathDecisionController.cs — GCD 管理 & 道具使用
        //
        // 弹窗触发与生命周期:
        //   - PlayerDeathDecisionController.TryHandleZeroHealth() 拦截死亡事件
        //   - 调用 PlayerDeathReviveUI.Show() → 关闭所有已开窗口 → 实例化 prefab → Init()
        //   - DontDestroyOnLoad，显示期间屏蔽玩家输入（PlayerInputRouter.enabled=false）
        //   - 关闭：CloseSelf() → 播放收缩动画 → Destroy
        //
        // 按钮交互规则（UI 设计统一标准）:
        //   - 所有可交互按钮（含左右箭头 ArrowL/ArrowR）必须挂 SkyPrisonUIButtonFeedback.Attach()
        //   - hover → 冷绿半透 (0.42,0.92,0.68,0.55)；点击 → 亮冷绿闪 (0.62,1.00,0.82,1.0)；离开还原白
        //   - 箭头按钮：无边框，只有扁平字符 < >，字号 60px，颜色浅灰 (0.72,0.72,0.76)
        //   - 箭头区域：细高条（宽约 8%，高 15%~85%），单道具时 SetActive(false) 隐藏
        //
        // 物品图标与数量:
        //   - 图标尺寸 120×120px（居中偏上 offset.y=20）
        //   - 数量 ×N 叠在图标右下角内：anchor=(1,0), pivot=(1,0), offset=(-4,0), size=(72,30)
        //   - 数量格式 <size=65%>×</size>N（× 缩小 65%，与数字共基线，BottomRight 对齐）
        //   - 数量颜色：浅灰白 (0.88,0.88,0.90)
        //   - GCD 中：图标变灰 color=(0.28,0.28,0.28)，冷绿选框隐藏，"使用该道具"按钮文字改为"（冷却中）"
        //
        // 复活 GCD 规则:
        //   - 复活道具使用后触发 共享 GCD（s_ReviveSharedGCDExpiry），所有复活道具同步等待
        //   - 复活 GCD 与普通快捷道具 GCD 完全隔离，互不影响
        //   - 死亡状态期间不刷新道具 GCD 状态（不允许玩家等到 GCD 结束再复活）
        //
        // 颜色规范:
        //   - 标题"生命维持警告"：暗红 (0.75,0.42,0.42,1.0)
        //   - 警告文"死亡将会造成...损失"：暗红 (0.75,0.42,0.42,0.90)
        //   - 返回据点按钮文字：同暗红 (0.75,0.42,0.42)
        //   - 所有红色统一使用上述暗红，禁止混用高饱和红 (0.93,0.26,0.16)
        //
        // Workbench 注意:
        //   - ApplyGlobalStyleToEditingInstance 对 DeathRevive prefab 只应用字体，跳过颜色覆盖
        //   - EnsureCurrentDeathReviveContents 重建后必须调 MarkDirty()+MarkPreviewDirty() 才会刷新预览
        //   - 修改 builder 后需要在 Workbench "恢复默认死亡弹窗结构..." → "保存" 同步到 Resources

        // ── 输入设备自动切换 & 提示条动态图标 ─────────────────────────────────────────
        //
        // SkyPrisonInputDeviceTracker (UI/InputPrompt/SkyPrisonInputDeviceTracker.cs):
        //   - 单例 MonoBehaviour，由 SkyPrisonRuntimeUIDriver.Awake 创建(DontDestroyOnLoad)。
        //   - 每 0.1s 检测一次：有键鼠输入(anyKey / MouseAxis) → KeyboardMouse；
        //     有手柄轴/按钮输入 → Gamepad。无明确输入时维持当前状态，不乱切。
        //   - 关键原则：只看玩家最近"实际操作"哪个设备，不只看手柄是否接入。
        //     接了手柄但在用键盘 → 继续显示键盘图标；切到摇杆/按键 → 自动切手柄图标。
        //   - 设备切换时发出 OnDeviceFamilyChanged(prev, next) 静态事件。
        //
        // SkyPrisonWindowHintBar (Inventory/SkyPrisonWindowHintBar.cs):
        //   - Show() 时订阅 OnDeviceFamilyChanged；Show(null) / Clear() 时取消订阅。
        //   - 收到切换事件立即重绘当前 _baseHints（上下文层不自动刷新，由上下文调用方负责）。
        //   - Render() 读 DeviceTracker.Current 决定显示键鼠还是手柄图标。
        //   - Icon 型提示：gamepad=false 时显示 iconKey，gamepad=true 时显示 gamepadIconKey；
        //     某侧 key 为空则该模式下整条隐藏，不混合显示。
        //   - GamepadIcon 型提示：只在 gamepad=true 时显示，反之隐藏。
        //   - Action 型提示：gamepad 模式下若解析不到手柄图标则整条隐藏（不混键鼠图标进来）。
        //   - 死亡弹窗 _hints 用 Icon/GamepadIcon 分别描述，不用 Action 型，避免 Action 未绑手柄被过滤。
        //
        // SkyPrisonQuickItemPromptStrip (UI/InputPrompt/SkyPrisonQuickItemPromptStrip.cs):
        //   - OnEnable 订阅 OnDeviceFamilyChanged → HandleDeviceFamilyChanged → RefreshPromptSprites。
        //   - RefreshPromptSprites 原来读 HasConnectedGamepad()，已改为读 DeviceTracker.Current，
        //     与 HintBar 行为一致（同一个 "最近使用" 标准）。
        //
        // 图标 key 规范（SkyPrisonInputPromptIconDatabaseSeeder 注册）:
        //   - 方向键: "gamepad/left" "gamepad/right" "gamepad/up" "gamepad/down"
        //   - 面按键: "gamepad/xbox/a" "gamepad/xbox/b" "gamepad/xbox/x" "gamepad/xbox/y"
        //             "gamepad/playstation/cross" "gamepad/playstation/circle" 等
        //   - 肩键:   "gamepad/l1" "gamepad/r1" "gamepad/l2" "gamepad/r2"
        //   - 禁止写 "gamepad/dpad/left" 等不存在的 key，会静默降级为纯文字。
        //
        // 游戏内改键接入（待实现）:
        //   - 图标读取是「现读现用」的（TryResolveActionIcon / ResolveKeyText 每次 Render 都读当前绑定），
        //     所以只要触发重绘就能显示正确新键，不需要改读取逻辑。
        //   - 缺的只是「改键 → 触发重绘」这一步。做法：
        //     1. SkyPrisonInputSettings 加 `public static event Action OnBindingChanged;`
        //     2. 改键生效处调 `SkyPrisonInputSettings.OnBindingChanged?.Invoke()`
        //     3. SkyPrisonWindowHintBar 和 SkyPrisonQuickItemPromptStrip 各订阅它，收到后重绘。
        //   - 当前状态（2026-06-21）：只有开发端初始按键设置，游戏内改键尚未开发，暂不接入。
        //
        // Do not:
        //   - 不要绕过 DeviceTracker 自行调 HasConnectedGamepad() 决定图标家族，会导致行为不一致。
        //   - 不要在 HintBar Render() 里混合不同设备家族的图标（整条提示统一一套）。

        // ============================================================
        // 全局标准：窗口背景高斯模糊（磨砂）实现方法 — 所有窗口统一用这套
        // ============================================================
        //
        // 适用范围：任何需要"截屏 + 磨砂/高斯模糊背景"的窗口（背包、暂停菜单、
        // 存档选择器等），今后新窗口要做同类效果一律照抄这套算法，不要各写各的。
        //
        // 参考实现：PauseMenuController.cs → CaptureAndBlurScreen()
        //（历史实现：Core/Inventory/SkyPrisonInventoryBlurBackground.cs 用的是旧版
        //  固定降 6 次再一步放大回全屏的写法，已确认在高分辨率/高DPI下有问题，
        //  待办：找机会把它也换成下面这套。）
        //
        // 截图来源:
        //   - 必须用 Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture()，
        //     不要手动按 Screen.width/Screen.height 建 RenderTexture 再用
        //     ScreenCapture.CaptureScreenshotIntoRenderTexture(rt) 去填——
        //     编辑器高 DPI 缩放下两者尺寸对不上，会出现"实际截到的画面比目标RT
        //     小一圈、糊内容全挤在左下角、其余留黑"的错位。CaptureScreenshotAsTexture
        //     返回的纹理尺寸就是真实截图尺寸，用它的 shot.width/shot.height 建后续 RT。
        //
        // 调用时机:
        //   - 必须在协程里先 yield return new WaitForEndOfFrame() 再截图，不能跟其他
        //     UI 隐藏操作同一帧就直接截——同帧截到的是渲染中途/上一帧画面，某些 Game
        //     视图缩放配置下会导致截出来的图基本空白、看起来"完全没有模糊背景"。
        //
        // 模糊算法（降采样 + 逐级放大金字塔，类似 Kawase/dual-filter blur）:
        //   1. 先把截图整体 Blit 到一张 960px 长边基准图（baseW/baseH，按截图长宽比算）。
        //   2. 从基准图开始反复腰斩降采样（每次 curW/2, curH/2），直到长边降到
        //      MinBlurEdge（12px）以下为止，每降一级都记录尺寸到 downSizes 链表里。
        //   3. 再按 downSizes 链表反过来逐级放大回去（每一步放大不超过 2 倍），
        //      一路走回 960px 基准图那一级为止。
        //   4. 最后从 960px 基准图一次性 Blit 到真实全屏尺寸（1080p 约 2 倍，
        //      4K 约 4 倍），这一步跳变足够小，双线性插值不会露馅。
        //
        // 为什么必须逐级放大、不能一步怼到底:
        //   - 早期写法是"降到很小（十几像素）之后一步直接放大回全屏"，几十像素
        //     单步放大几百倍，双线性插值会显出网格状的"钻石形"过渡痕迹（像马赛克），
        //     或者干脆看起来就是"缩小糊了再放大"，而不是真正柔和的高斯扩散感。
        //   - 逐级放大（每步≤2倍）能让每一步插值都足够细腻，叠加起来才有真实的
        //     高斯/塔式模糊观感。
        //
        // 参数经验值:
        //   - 960px 长边基准图 + MinBlurEdge=12px，在 1080p~4K 都测试过，观感一致。
        //   - 需要更强/更弱模糊时，只调 MinBlurEdge（越小越糊），不要改回固定
        //     降采样次数或跳过逐级放大。
        //
        // Do not:
        //   - 不要在降采样/放大链路里混用 Point 滤波，每一级 RenderTexture 都要
        //     显式设 filterMode = FilterMode.Bilinear。
        //   - 不要把最后一步"基准图→全屏"的放大倍数做得比 4~5 倍更大——如果因为
        //     目标分辨率极高导致这一步跳变太大，应该调高 SrcLongEdge 基准图尺寸，
        //     而不是跳过中间的逐级放大链路。

    }
}
#endif
