using System;
using UnityEngine;

[Serializable]
public class RuntimeStatusEntry
{
    public string statusKey = string.Empty;
    public StatusDefinition definition;
    public GameObject sourceUnit;
    public int stackCount = 1;
    public float remainingDuration = 0f;
    public float displayTotalDuration = 0f;
    public float triggerTickTimer = 0f;
    public float dotTickTimer = 0f;
    public bool pendingRemove = false;

    public bool IsValid => definition != null && !string.IsNullOrWhiteSpace(statusKey);

    public bool IsExpired
    {
        get
        {
            if (definition == null)
                return true;

            if (definition.durationType == StatusDurationType.Permanent)
                return false;

            return remainingDuration <= 0f;
        }
    }

    public bool HasVisibleLimitedDuration
    {
        get
        {
            if (definition == null)
                return false;
            if (definition.durationType != StatusDurationType.Timed)
                return false;
            if (float.IsInfinity(displayTotalDuration))
                return false;
            return displayTotalDuration > 0f;
        }
    }

    public void Initialize(StatusDefinition statusDefinition, GameObject source, int initialStacks, float durationOverride = -1f)
    {
        definition = statusDefinition;
        statusKey = statusDefinition != null ? statusDefinition.statusId ?? string.Empty : string.Empty;
        sourceUnit = source;
        stackCount = Mathf.Max(1, initialStacks);
        remainingDuration = ResolveInitialDuration(statusDefinition, durationOverride);
        displayTotalDuration = remainingDuration;
        triggerTickTimer = 0f;
        dotTickTimer = 0f;
        pendingRemove = false;
    }

    public void ApplyDeltaTime(float deltaTime)
    {
        if (definition == null || deltaTime <= 0f)
            return;

        if (definition.durationType == StatusDurationType.Timed)
            remainingDuration = Mathf.Max(0f, remainingDuration - deltaTime);

        if (definition.tickInterval > 0f)
            triggerTickTimer += deltaTime;

        if (definition.enableDot && definition.dotTickInterval > 0f)
            dotTickTimer += deltaTime;
    }

    public bool TryConsumeTriggerTick(out int tickCount)
    {
        tickCount = 0;
        if (definition == null || definition.tickInterval <= 0f)
            return false;

        while (triggerTickTimer >= definition.tickInterval)
        {
            triggerTickTimer -= definition.tickInterval;
            tickCount++;
        }

        return tickCount > 0;
    }

    public bool TryConsumeDotTick(out int tickCount)
    {
        tickCount = 0;
        if (definition == null || !definition.enableDot || definition.dotTickInterval <= 0f)
            return false;

        while (dotTickTimer >= definition.dotTickInterval)
        {
            dotTickTimer -= definition.dotTickInterval;
            tickCount++;
        }

        return tickCount > 0;
    }

    public void RefreshDuration(float durationOverride = -1f)
    {
        if (definition == null)
            return;

        float nextDuration = ResolveInitialDuration(definition, durationOverride);

        switch (definition.durationUpdateMode)
        {
            case StatusDurationUpdateMode.Keep:
                break;

            case StatusDurationUpdateMode.Additive:
                remainingDuration += nextDuration;
                if (HasFiniteDuration(nextDuration))
                {
                    if (HasFiniteDuration(displayTotalDuration))
                        displayTotalDuration += nextDuration;
                    else
                        displayTotalDuration = remainingDuration;
                }
                break;

            case StatusDurationUpdateMode.Override:
            default:
                remainingDuration = nextDuration;
                displayTotalDuration = nextDuration;
                break;
        }

        if (definition.maxDuration > 0f)
        {
            remainingDuration = Mathf.Min(remainingDuration, definition.maxDuration);
            if (HasFiniteDuration(displayTotalDuration))
                displayTotalDuration = Mathf.Min(displayTotalDuration, definition.maxDuration);
        }

        if (!HasFiniteDuration(displayTotalDuration) && HasFiniteDuration(remainingDuration))
            displayTotalDuration = remainingDuration;

        if (HasFiniteDuration(displayTotalDuration) && displayTotalDuration < remainingDuration)
            displayTotalDuration = remainingDuration;
    }

    public void RefreshStacks(int requestedStacks)
    {
        if (definition == null)
            return;

        int incoming = Mathf.Max(1, requestedStacks);
        int nextStacks;

        if (!definition.canStack)
        {
            nextStacks = 1;
        }
        else
        {
            switch (definition.stackUpdateMode)
            {
                case StatusStackUpdateMode.Keep:
                    nextStacks = stackCount;
                    break;
                case StatusStackUpdateMode.Override:
                    nextStacks = incoming;
                    break;
                case StatusStackUpdateMode.AddValue:
                    nextStacks = stackCount + incoming;
                    break;
                case StatusStackUpdateMode.AddOne:
                default:
                    nextStacks = stackCount + 1;
                    break;
            }
        }

        int maxStack = Mathf.Max(1, definition.maxStack);
        stackCount = Mathf.Clamp(nextStacks, 1, maxStack);
    }

    private static bool HasFiniteDuration(float value)
    {
        return !float.IsInfinity(value) && !float.IsNaN(value) && value > 0f;
    }

    private static float ResolveInitialDuration(StatusDefinition definition, float durationOverride)
    {
        if (definition == null)
            return 0f;

        if (definition.durationType == StatusDurationType.Instant)
            return 0f;

        if (definition.durationType == StatusDurationType.Permanent)
            return float.PositiveInfinity;

        float duration = durationOverride >= 0f ? durationOverride : definition.baseDuration;
        if (definition.maxDuration > 0f)
            duration = Mathf.Min(duration, definition.maxDuration);

        return Mathf.Max(0f, duration);
    }
}
