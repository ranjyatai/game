using UnityEngine;
using Spine;
using Spine.Unity;

/// <summary>
/// V4 - 2026-05-29
/// Footstep relay for Spine animation events.
/// 
/// Fix:
/// - Auto resolve now avoids Outline / Proxy skeletons.
/// - Prefers the real visual Spine skeleton under VisualRoot / SpineRoot.
/// - Re-subscribes safely when the resolved SkeletonAnimation changes.
/// - Adds debug logs for all received Spine event names when Debug Logs is enabled.
/// - Rejects cross-unit FootstepEmitter references and always rebinds to the local unit emitter.
/// - Adds explicit runtime Configure(emitter, skeleton) for Build-safe binding after visual refresh.
/// 
/// This relay only forwards Spine Event names. It does not decide which audio package to use.
/// </summary>
[DisallowMultipleComponent]
public class SpineFootstepEventRelay : MonoBehaviour
{
    [SerializeField] private SkeletonAnimation skeletonAnimation;
    [SerializeField] private UnitFootstepAudioEmitter footstepEmitter;
    [SerializeField] private bool autoResolveOnEnable = true;
    [SerializeField] private bool debugLogs = false;

    private bool subscribed;
    private SkeletonAnimation subscribedSkeleton;

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        if (autoResolveOnEnable)
            ResolveReferences();

        TrySubscribe();
    }

    private void Start()
    {
        if (!subscribed)
        {
            ResolveReferences();
            TrySubscribe();
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void Configure(UnitFootstepAudioEmitter emitter)
    {
        footstepEmitter = emitter;
        ResolveReferences();
        TrySubscribe();
    }

    public void Configure(UnitFootstepAudioEmitter emitter, SkeletonAnimation targetSkeleton)
    {
        footstepEmitter = emitter;

        if (targetSkeleton != null)
            SetSkeletonAnimation(targetSkeleton);

        ResolveEmitterOnly();
        TrySubscribe();

        if (debugLogs)
        {
            string skel = skeletonAnimation != null ? GetTransformPath(skeletonAnimation.transform) : "null";
            string emitterPath = footstepEmitter != null ? GetTransformPath(footstepEmitter.transform) : "null";
            Debug.Log($"[SpineFootstepEventRelay] {name}: configured explicit runtime binding. skeleton={skel}, emitter={emitterPath}, subscribed={subscribed}", this);
        }
    }

    public void ForceRefreshAndSubscribe()
    {
        ResolveReferences();
        TrySubscribe();
    }

    public void SetSkeletonAnimation(SkeletonAnimation target)
    {
        if (skeletonAnimation == target)
            return;

        Unsubscribe();
        skeletonAnimation = target;
        TrySubscribe();
    }

    [ContextMenu("Resolve References")]
    public void ResolveReferences()
    {
        SkeletonAnimation best = FindBestSkeletonAnimation();

        if (best != null && best != skeletonAnimation)
        {
            if (debugLogs)
                Debug.Log($"[SpineFootstepEventRelay] {name}: resolved SkeletonAnimation = {GetTransformPath(best.transform)}", this);

            SetSkeletonAnimation(best);
        }

        UnitFootstepAudioEmitter localEmitter = FindLocalFootstepEmitter();

        // Never keep a cross-unit emitter reference.  In Build the object creation order can differ,
        // and a global/foreign binding makes one unit forward events into another unit's emitter.
        if (footstepEmitter != null && !IsEmitterLocalToThisRelay(footstepEmitter))
        {
            Debug.LogWarning($"[SpineFootstepEventRelay] {name}: rejected foreign FootstepEmitter = {GetTransformPath(footstepEmitter.transform)}. Rebinding to local emitter = {(localEmitter != null ? GetTransformPath(localEmitter.transform) : "null")}", this);
            footstepEmitter = null;
        }

        if (localEmitter != null && footstepEmitter != localEmitter)
        {
            footstepEmitter = localEmitter;
            if (debugLogs)
                Debug.Log($"[SpineFootstepEventRelay] {name}: resolved local FootstepEmitter = {GetTransformPath(footstepEmitter.transform)}", this);
        }
    }

    [ContextMenu("Log Current Binding")]
    public void LogCurrentBinding()
    {
        string skel = skeletonAnimation != null ? GetTransformPath(skeletonAnimation.transform) : "null";
        string emitter = footstepEmitter != null ? GetTransformPath(footstepEmitter.transform) : "null";
        Debug.Log($"[SpineFootstepEventRelay] {name}: skeleton={skel}, emitter={emitter}, subscribed={subscribed}", this);
    }


    private void ResolveEmitterOnly()
    {
        UnitFootstepAudioEmitter localEmitter = FindLocalFootstepEmitter();

        if (footstepEmitter != null && !IsEmitterLocalToThisRelay(footstepEmitter))
        {
            Debug.LogWarning($"[SpineFootstepEventRelay] {name}: rejected foreign FootstepEmitter = {GetTransformPath(footstepEmitter.transform)}. Rebinding to local emitter = {(localEmitter != null ? GetTransformPath(localEmitter.transform) : "null")}", this);
            footstepEmitter = null;
        }

        if (localEmitter != null && footstepEmitter != localEmitter)
        {
            footstepEmitter = localEmitter;
            if (debugLogs)
                Debug.Log($"[SpineFootstepEventRelay] {name}: resolved local FootstepEmitter = {GetTransformPath(footstepEmitter.transform)}", this);
        }
    }


    private UnitFootstepAudioEmitter FindLocalFootstepEmitter()
    {
        // 1) Same GameObject is the normal runtime-applier layout.
        UnitFootstepAudioEmitter emitter = GetComponent<UnitFootstepAudioEmitter>();
        if (emitter != null)
            return emitter;

        // 2) Relay can be placed on a child under the unit root.
        emitter = GetComponentInParent<UnitFootstepAudioEmitter>();
        if (emitter != null)
            return emitter;

        // 3) Relay can be placed on the unit root while the emitter sits under AudioRoot.
        emitter = GetComponentInChildren<UnitFootstepAudioEmitter>(true);
        if (emitter != null)
            return emitter;

        return null;
    }

    private bool IsEmitterLocalToThisRelay(UnitFootstepAudioEmitter emitter)
    {
        if (emitter == null)
            return false;

        Transform emitterTransform = emitter.transform;
        Transform relayTransform = transform;

        if (emitterTransform == relayTransform)
            return true;

        // Parent/child relationship is safe for all supported layouts.
        if (emitterTransform.IsChildOf(relayTransform))
            return true;

        if (relayTransform.IsChildOf(emitterTransform))
            return true;

        return false;
    }

    private SkeletonAnimation FindBestSkeletonAnimation()
    {
        SkeletonAnimation[] all = GetComponentsInChildren<SkeletonAnimation>(true);
        if (all == null || all.Length == 0)
            return null;

        SkeletonAnimation best = null;
        int bestScore = int.MinValue;

        for (int i = 0; i < all.Length; i++)
        {
            SkeletonAnimation candidate = all[i];
            if (candidate == null)
                continue;

            int score = ScoreSkeletonCandidate(candidate);
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    private int ScoreSkeletonCandidate(SkeletonAnimation candidate)
    {
        string path = GetTransformPath(candidate.transform).ToLowerInvariant();
        string n = candidate.name.ToLowerInvariant();

        int score = 0;

        // Strongly avoid non-authoritative visual clones.
        if (path.Contains("outlineproxy") || path.Contains("outline_proxy") || path.Contains("outline proxy"))
            score -= 1000;
        if (path.Contains("proxy") || path.Contains("outline") || path.Contains("occlusion") || path.Contains("shadow"))
            score -= 300;

        // Prefer the actual visual Spine hierarchy.
        if (path.Contains("visualroot"))
            score += 200;
        if (path.Contains("spineroot"))
            score += 200;
        if (n.Contains("spine"))
            score += 80;
        if (path.Contains("spine gameobject") || path.Contains("axia_base"))
            score += 120;

        // Prefer enabled active skeletons, but still allow inactive during setup.
        if (candidate.gameObject.activeInHierarchy)
            score += 30;
        if (candidate.enabled)
            score += 20;

        // If the current user-assigned skeleton is a real one, do not fight it too hard.
        if (candidate == skeletonAnimation && !IsProbablyProxy(candidate))
            score += 500;

        return score;
    }

    private bool IsProbablyProxy(SkeletonAnimation candidate)
    {
        if (candidate == null)
            return false;

        string path = GetTransformPath(candidate.transform).ToLowerInvariant();
        return path.Contains("outlineproxy")
            || path.Contains("outline_proxy")
            || path.Contains("outline proxy")
            || path.Contains("proxy")
            || path.Contains("outline")
            || path.Contains("occlusion")
            || path.Contains("shadow");
    }

    private void TrySubscribe()
    {
        if (skeletonAnimation == null || skeletonAnimation.AnimationState == null)
            return;

        // If the target changed, clear the old subscription first.
        if (subscribed && subscribedSkeleton != skeletonAnimation)
            Unsubscribe();

        if (subscribed)
            return;

        skeletonAnimation.AnimationState.Event += HandleSpineEvent;
        subscribedSkeleton = skeletonAnimation;
        subscribed = true;

        if (debugLogs)
            Debug.Log($"[SpineFootstepEventRelay] {name}: subscribed Spine events from {GetTransformPath(skeletonAnimation.transform)}.", this);
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (subscribedSkeleton != null && subscribedSkeleton.AnimationState != null)
            subscribedSkeleton.AnimationState.Event -= HandleSpineEvent;

        subscribedSkeleton = null;
        subscribed = false;
    }

    private void HandleSpineEvent(TrackEntry trackEntry, Spine.Event spineEvent)
    {
        if (spineEvent == null || spineEvent.Data == null)
            return;

        string eventName = spineEvent.Data.Name;

        if (debugLogs)
            Debug.Log($"[SpineFootstepEventRelay] {name}: Spine Event = {eventName}", this);

        if (footstepEmitter == null)
            ResolveReferences();

        if (footstepEmitter == null)
            return;

        footstepEmitter.HandleSpineEventName(eventName);
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null)
            return "null";

        string path = t.name;
        Transform p = t.parent;
        while (p != null)
        {
            path = p.name + "/" + path;
            p = p.parent;
        }

        return path;
    }
}
