using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Controls the PlayerStatus module visibility as a single authored module.
    /// V1: weak-HUD rule. Non-combat + full HP/LP fades out; combat or non-full vitals fades in.
    /// Keep this on PlayerStatusArea so it affects the RT output RawImage and does not fight individual bar graphics.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonHUDCombatVisibilityController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private CanvasGroup targetCanvasGroup;

        [Header("Auto Visibility")]
        [SerializeField] private bool enableAutoFadeWhenNonCombatFull = true;
        [SerializeField] private bool showWhenHpNotFull = true;
        [SerializeField] private bool showWhenLpNotFull = true;
        [SerializeField] private bool simulatedCombatState = false;
        [SerializeField] private bool forceVisible = false;

        [Header("Timing")]
        [SerializeField, Min(0f)] private float fadeInDuration = 0.18f;
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.35f;
        [SerializeField, Min(0f)] private float combatExitHoldDuration = 2.00f;
        [SerializeField, Range(0f, 1f)] private float minimumAlpha = 0.35f;

        [Header("Runtime - Read Only")]
        [SerializeField] private bool runtimeCombatState;
        [SerializeField] private float authoredCanvasGroupAlpha = 1f;
        [SerializeField] private float visibilityAlpha = 1f;
        [SerializeField] private float hpRatio = 1f;
        [SerializeField] private float lpRatio = 1f;
        [SerializeField] private bool runtimeShouldShow = true;
        [SerializeField] private string runtimeReason = "Visible";

        private float lastCombatLikeTime = -9999f;
        private bool authoredAlphaCaptured;

        public bool EnableAutoFadeWhenNonCombatFull
        {
            get => enableAutoFadeWhenNonCombatFull;
            set => enableAutoFadeWhenNonCombatFull = value;
        }

        public bool SimulatedCombatState
        {
            get => simulatedCombatState;
            set => simulatedCombatState = value;
        }

        public bool ShowWhenHpNotFull
        {
            get => showWhenHpNotFull;
            set => showWhenHpNotFull = value;
        }

        public bool ShowWhenLpNotFull
        {
            get => showWhenLpNotFull;
            set => showWhenLpNotFull = value;
        }

        public bool ForceVisible
        {
            get => forceVisible;
            set => forceVisible = value;
        }

        public float FadeInDuration
        {
            get => fadeInDuration;
            set => fadeInDuration = Mathf.Max(0f, value);
        }

        public float FadeOutDuration
        {
            get => fadeOutDuration;
            set => fadeOutDuration = Mathf.Max(0f, value);
        }

        public float CombatExitHoldDuration
        {
            get => combatExitHoldDuration;
            set => combatExitHoldDuration = Mathf.Max(0f, value);
        }

        public float MinimumAlpha
        {
            get => minimumAlpha;
            set => minimumAlpha = Mathf.Clamp01(value);
        }

        private void Awake()
        {
            ResolveTargetCanvasGroup();
            CaptureAuthoredAlphaFromGroup(false);
            visibilityAlpha = ShouldShowNow(out runtimeReason) ? 1f : Mathf.Clamp01(minimumAlpha);
            ApplyAlpha();
        }

        private void OnEnable()
        {
            ResolveTargetCanvasGroup();
            CaptureAuthoredAlphaFromGroup(false);
            ApplyAlpha();
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            // 读条揭幕期间，LoadingScreenUI 独占这个 CanvasGroup 的 alpha 写入权。
            // 这里如果继续每帧写 alpha，会和揭幕的淡入动画互相打架，造成闪烁/回暗。
            if (SceneLoader.IsAwaitingReveal || SceneLoader.IsRevealing)
                return;

            // 暂停菜单等"外部阻塞"系统也会直接把这个 CanvasGroup.alpha 设成 0 来隐藏 HUD——
            // 之前没检查这个标记，这里每帧都按"非战斗+满血该淡出/该显示"的逻辑把 alpha
            // 写回去，两边同时写同一个字段，暂停这边永远打不过，表现为"HUD 收不干净"。
            if (SkyPrisonWindowManager_V1.AnyBlockingWindowOpen)
                return;

            Tick(Time.unscaledDeltaTime);
        }

        public void SetRuntimeCombatState(bool inCombat)
        {
            runtimeCombatState = inCombat;
            if (inCombat)
                lastCombatLikeTime = Now;
        }

        public void SetVitals(float currentHp, float maxHp, float currentLp, float maxLp)
        {
            hpRatio = SafeRatio(currentHp, maxHp);
            lpRatio = SafeRatio(currentLp, maxLp);
        }

        public void SetHp(float currentHp, float maxHp)
        {
            hpRatio = SafeRatio(currentHp, maxHp);
        }

        public void SetLp(float currentLp, float maxLp)
        {
            lpRatio = SafeRatio(currentLp, maxLp);
        }

        public void PreviewImmediate(float currentHp, float maxHp, float currentLp, float maxLp, bool previewCombat)
        {
            ResolveTargetCanvasGroup();
            CaptureAuthoredAlphaFromGroup(false);
            hpRatio = SafeRatio(currentHp, maxHp);
            lpRatio = SafeRatio(currentLp, maxLp);
            simulatedCombatState = previewCombat;
            bool show = ShouldShowNow(out runtimeReason);
            runtimeShouldShow = show;
            visibilityAlpha = show ? 1f : Mathf.Clamp01(minimumAlpha);
            ApplyAlpha();
        }

        public void ResetToAuthoredAlphaForPrefabSave()
        {
            ResolveTargetCanvasGroup();
            if (targetCanvasGroup == null)
                return;

            targetCanvasGroup.alpha = Mathf.Clamp01(authoredCanvasGroupAlpha);
        }

        public void CaptureAuthoredAlphaFromGroup(bool force)
        {
            ResolveTargetCanvasGroup();
            if (targetCanvasGroup == null)
                return;

            if (!force && authoredAlphaCaptured)
                return;

            // When not playing, the workbench/prefab value is the authored design alpha.
            // At runtime this is called before this controller starts driving the group.
            authoredCanvasGroupAlpha = Mathf.Clamp01(targetCanvasGroup.alpha);
            authoredAlphaCaptured = true;
        }

        private void Tick(float dt)
        {
            ResolveTargetCanvasGroup();
            CaptureAuthoredAlphaFromGroup(false);

            bool show = ShouldShowNow(out runtimeReason);
            runtimeShouldShow = show;

            float target = show ? 1f : Mathf.Clamp01(minimumAlpha);
            float duration = show ? fadeInDuration : fadeOutDuration;
            if (duration <= 0.0001f)
                visibilityAlpha = target;
            else
                visibilityAlpha = Mathf.MoveTowards(visibilityAlpha, target, dt / duration);

            ApplyAlpha();
        }

        private bool ShouldShowNow(out string reason)
        {
            if (!enableAutoFadeWhenNonCombatFull)
            {
                reason = "Auto fade disabled";
                return true;
            }

            if (forceVisible)
            {
                reason = "Force visible";
                return true;
            }

            if (runtimeCombatState || simulatedCombatState)
            {
                lastCombatLikeTime = Now;
                reason = runtimeCombatState ? "Runtime combat" : "Simulated combat";
                return true;
            }

            if (showWhenHpNotFull && hpRatio < 0.999f)
            {
                reason = "HP not full";
                return true;
            }

            if (showWhenLpNotFull && lpRatio < 0.999f)
            {
                reason = "LP not full";
                return true;
            }

            if (combatExitHoldDuration > 0f && Now - lastCombatLikeTime < combatExitHoldDuration)
            {
                reason = "Combat exit hold";
                return true;
            }

            reason = "Non-combat and full";
            return false;
        }

        private void ApplyAlpha()
        {
            if (targetCanvasGroup == null)
                return;

            targetCanvasGroup.alpha = Mathf.Clamp01(authoredCanvasGroupAlpha) * Mathf.Clamp01(visibilityAlpha);
        }

        private void ResolveTargetCanvasGroup()
        {
            if (targetCanvasGroup == null)
                targetCanvasGroup = GetComponent<CanvasGroup>();
            if (targetCanvasGroup == null)
                targetCanvasGroup = GetComponentInParent<CanvasGroup>();
        }

        private float Now => Application.isPlaying ? Time.unscaledTime : (float)Time.realtimeSinceStartup;

        private static float SafeRatio(float current, float max)
        {
            if (max <= 0.0001f)
                return 1f;
            return Mathf.Clamp01(current / max);
        }
    }
}
