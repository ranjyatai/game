public interface IAIScenePickCapableSlotHandler
{
    bool SupportsScenePick(AIScenePickKind kind);

    AIScenePickRequest BuildScenePickRequest(
        string slotId,
        LogicSentenceTemplate.SlotDefinition slotDef,
        LogicSlotValue currentValue,
        AIScenePickRestoreState restoreState
    );

    void ApplyScenePickResult(
        LogicSlotValue targetValue,
        AIScenePickResult result
    );
}
