using System.Collections;
using UnityEngine;
using Spine;
using Spine.Unity;

/// <summary>
/// V2 - 2026-05-29
/// Runtime Spine skin combiner.
///
/// Build fix:
/// - Applying / initializing a SkeletonAnimation can recreate AnimationState.
/// - SpineFootstepEventRelay subscribes to AnimationState.Event, so it must re-subscribe after skin application.
/// - This script keeps the future runtime skin-change workflow, but notifies the local footstep relay after the skin is applied.
///
/// This script does not choose footstep audio packages. It only preserves the Spine event subscription after dress-up.
/// </summary>
public class SpineDressUpTest : MonoBehaviour
{
    public SkeletonAnimation skeletonAnimation;
    public string[] skinNames;

    [Header("运行时换装")]
    [SerializeField] private bool applyOnStart = true;

    [Tooltip("通常不要强制重新初始化。强制初始化会重建 Spine AnimationState，可能让脚步事件订阅失效；脚本会在之后自动重绑。")]
    [SerializeField] private bool forceInitializeBeforeApply = false;

    [Tooltip("缺失皮肤时是否输出 Warning。正式 Build 如果太刷屏，可以关闭。")]
    [SerializeField] private bool warnMissingSkins = true;

    [Header("脚步声事件重绑")]
    [Tooltip("换装完成后，重新绑定同单位的 SpineFootstepEventRelay，防止 Build 中 Initialize/换装后脚步事件订阅丢失。")]
    [SerializeField] private bool refreshFootstepRelayAfterApply = true;

    [Tooltip("换装完成后延迟若干帧再重绑一次，用于兜底其它 Start/初始化脚本后续刷新 Spine 的情况。")]
    [SerializeField] private int delayedRefreshFrames = 2;

    private Coroutine delayedRefreshRoutine;

    private void Start()
    {
        if (!applyOnStart)
            return;

        if (skeletonAnimation == null)
            skeletonAnimation = GetComponent<SkeletonAnimation>();

        ApplyCombinedSkin();
    }

    [ContextMenu("Apply Combined Skin")]
    public void ApplyCombinedSkin()
    {
        if (skeletonAnimation == null)
            skeletonAnimation = GetComponent<SkeletonAnimation>();

        if (skeletonAnimation == null)
        {
            Debug.LogError("SkeletonAnimation not found.", this);
            return;
        }

        // Do not always force Initialize(true).
        // Initialize(true) rebuilds AnimationState and can detach AnimationState.Event listeners.
        // Only initialize when Skeleton is missing, unless explicitly forced for debugging.
        if (forceInitializeBeforeApply || skeletonAnimation.Skeleton == null)
            skeletonAnimation.Initialize(forceInitializeBeforeApply);

        Skeleton skeleton = skeletonAnimation.Skeleton;
        if (skeleton == null)
        {
            Debug.LogError("Skeleton is null.", this);
            return;
        }

        Skin combinedSkin = new Skin("runtime-combined");
        int addedSkinCount = 0;

        if (skinNames != null)
        {
            for (int i = 0; i < skinNames.Length; i++)
            {
                string skinName = skinNames[i];
                if (string.IsNullOrWhiteSpace(skinName))
                    continue;

                Skin foundSkin = skeleton.Data.FindSkin(skinName);
                if (foundSkin == null)
                {
                    if (warnMissingSkins)
                        Debug.LogWarning("Skin not found: " + skinName, this);
                    continue;
                }

                combinedSkin.AddSkin(foundSkin);
                addedSkinCount++;
            }
        }

        if (addedSkinCount > 0)
        {
            skeleton.SetSkin(combinedSkin);
            InvokeFirstNoArgMethod(skeleton, "SetupPoseSlots", "SetSlotsToSetupPose", "SetupPose", "SetToSetupPose");
            skeletonAnimation.AnimationState.Apply(skeleton);
            skeletonAnimation.Update(0f);
        }
        else if (warnMissingSkins)
        {
            Debug.LogWarning("Combined skin was not applied because no valid skin was found.", this);
        }

        Debug.Log("Combined skin applied. validSkinCount=" + addedSkinCount, this);

        RefreshFootstepRelayNowAndLater();
    }

    /// <summary>
    /// Runtime entry for future equipment / skin systems.
    /// Call this after changing skinNames, or pass a complete skin list directly.
    /// </summary>
    public void ApplyCombinedSkin(string[] newSkinNames)
    {
        skinNames = newSkinNames;
        ApplyCombinedSkin();
    }

    private void RefreshFootstepRelayNowAndLater()
    {
        if (!refreshFootstepRelayAfterApply)
            return;

        ForceRefreshLocalFootstepRelays();

        if (!isActiveAndEnabled)
            return;

        if (delayedRefreshRoutine != null)
            StopCoroutine(delayedRefreshRoutine);

        delayedRefreshRoutine = StartCoroutine(DelayedRefreshFootstepRelays());
    }

    private IEnumerator DelayedRefreshFootstepRelays()
    {
        int frames = Mathf.Max(0, delayedRefreshFrames);
        for (int i = 0; i < frames; i++)
            yield return null;

        ForceRefreshLocalFootstepRelays();
        delayedRefreshRoutine = null;
    }

    private void ForceRefreshLocalFootstepRelays()
    {
        // Usually the relay is on the unit root while this script is on the Spine object.
        SpineFootstepEventRelay relay = GetComponentInParent<SpineFootstepEventRelay>(true);
        if (relay != null)
        {
            relay.ForceRefreshAndSubscribe();
            return;
        }

        // Fallback for alternate layouts where the relay is placed below the same object.
        SpineFootstepEventRelay[] childRelays = GetComponentsInChildren<SpineFootstepEventRelay>(true);
        if (childRelays == null)
            return;

        for (int i = 0; i < childRelays.Length; i++)
        {
            if (childRelays[i] != null)
                childRelays[i].ForceRefreshAndSubscribe();
        }
    }

    private static void InvokeFirstNoArgMethod(object targetObject, params string[] methodNames)
    {
        if (targetObject == null || methodNames == null)
            return;

        System.Type type = targetObject.GetType();
        for (int i = 0; i < methodNames.Length; i++)
        {
            System.Reflection.MethodInfo method = type.GetMethod(
                methodNames[i],
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null,
                System.Type.EmptyTypes,
                null);

            if (method == null)
                continue;

            method.Invoke(targetObject, null);
            return;
        }
    }
}
