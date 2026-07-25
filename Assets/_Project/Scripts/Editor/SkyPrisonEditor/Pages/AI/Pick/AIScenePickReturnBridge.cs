using UnityEngine;

public static class AIScenePickReturnBridge
{
    public static bool CanBuildRequest(LogicSentenceTemplate.SlotDefinition slotDef)
    {
        if (slotDef == null)
            return false;

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slotDef.valueType);
        return handler is IAIScenePickCapableSlotHandler;
    }

    public static AIScenePickRequest BuildRequest(
        string slotId,
        LogicSentenceTemplate.SlotDefinition slotDef,
        LogicSlotValue currentValue,
        AIScenePickRestoreState restoreState,
        AIScenePickKind pickKind
    )
    {
        if (slotDef == null)
        {
            Debug.LogWarning("[AIScenePickReturnBridge] BuildRequest failed: slotDef is null.");
            return null;
        }

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slotDef.valueType);
        if (handler is not IAIScenePickCapableSlotHandler pickCapable)
        {
            Debug.LogWarning(
                $"[AIScenePickReturnBridge] BuildRequest failed: handler for {slotDef.valueType} does not support scene pick."
            );
            return null;
        }

        if (!pickCapable.SupportsScenePick(pickKind))
        {
            Debug.LogWarning(
                $"[AIScenePickReturnBridge] BuildRequest failed: handler for {slotDef.valueType} does not support pick kind {pickKind}."
            );
            return null;
        }

        return pickCapable.BuildScenePickRequest(slotId, slotDef, currentValue, restoreState);
    }

    public static bool ApplyResult(
        LogicSentenceTemplate.SlotDefinition slotDef,
        LogicSlotValue targetValue,
        AIScenePickResult result
    )
    {
        if (slotDef == null || targetValue == null || result == null)
        {
            Debug.LogWarning("[AIScenePickReturnBridge] ApplyResult failed: null input.");
            return false;
        }

        ILogicSlotHandler handler = LogicSlotHandlerRegistry.Get(slotDef.valueType);
        if (handler is not IAIScenePickCapableSlotHandler pickCapable)
        {
            Debug.LogWarning(
                $"[AIScenePickReturnBridge] ApplyResult failed: handler for {slotDef.valueType} does not support scene pick."
            );
            return false;
        }

        if (!pickCapable.SupportsScenePick(result.pickKind))
        {
            Debug.LogWarning(
                $"[AIScenePickReturnBridge] ApplyResult failed: handler for {slotDef.valueType} does not support result kind {result.pickKind}."
            );
            return false;
        }

        pickCapable.ApplyScenePickResult(targetValue, result);

        Debug.Log(
            $"[AIScenePickReturnBridge] ApplyResult success. " +
            $"slotValueType={slotDef.valueType}, pickKind={result.pickKind}, objectId={result.objectId}, objectName={result.objectName}"
        );

        return true;
    }
}
