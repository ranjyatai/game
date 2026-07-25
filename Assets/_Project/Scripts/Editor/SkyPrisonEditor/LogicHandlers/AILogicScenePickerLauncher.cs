using System;
using UnityEditor;

public static class AILogicScenePickLauncher
{
    private sealed class PendingPickContext
    {
        public LogicSentenceTemplate.SlotDefinition slotDef;
        public string slotId;
        public Action<LogicSlotValue> applyValueBack;
        public Func<LogicSlotValue> getCurrentValue;
        public AIScenePickRestoreState restoreState;
    }

    private static PendingPickContext pendingContext;
    private static bool subscribed;

    public static bool BeginFromLogicSlot(
        string slotId,
        LogicSentenceTemplate.SlotDefinition slotDef,
        Func<LogicSlotValue> getCurrentValue,
        Action<LogicSlotValue> applyValueBack,
        AIScenePickRestoreState restoreState,
        AIScenePickKind pickKind = AIScenePickKind.Unit)
    {
        if (string.IsNullOrWhiteSpace(slotId) || slotDef == null || getCurrentValue == null || applyValueBack == null)
            return false;

        if (AIScenePickCoordinator.HasActiveRequest)
            return false;

        LogicSlotValue currentValue = CloneValueSafe(getCurrentValue());

        AIScenePickRequest request = AIScenePickReturnBridge.BuildRequest(
            slotId,
            slotDef,
            currentValue,
            restoreState,
            pickKind
        );

        if (request == null)
            return false;

        pendingContext = new PendingPickContext
        {
            slotDef = slotDef,
            slotId = slotId,
            applyValueBack = applyValueBack,
            getCurrentValue = getCurrentValue,
            restoreState = restoreState
        };

        EnsureSubscribed();

        AIScenePickFocusGuard.BeginPreparing();
        bool started = AIScenePickCoordinator.TryBeginPick(request);
        if (!started)
            AIScenePickFocusGuard.EndAll();

        return started;
    }

    private static void EnsureSubscribed()
    {
        if (subscribed)
            return;

        AIScenePickCoordinator.OnPickCompleted += HandlePickCompleted;
        AIScenePickCoordinator.OnPickCancelled += HandlePickCancelled;
        subscribed = true;
    }

    private static void HandlePickCompleted(AIScenePickResult result, AIScenePickRequest request)
    {
        if (pendingContext == null)
            return;

        LogicSlotValue currentValue = CloneValueSafe(pendingContext.getCurrentValue());
        bool applied = AIScenePickReturnBridge.ApplyResult(
            pendingContext.slotDef,
            currentValue,
            result
        );

        if (applied)
        {
            // 1) 回写当前编辑值
            pendingContext.applyValueBack(currentValue);

            // 2) 关键修复：同步写回 restoreState 的 workingInstance
            ApplyValueToRestoreStateWorkingInstance(
                pendingContext.restoreState,
                pendingContext.slotId,
                currentValue
            );
        }

        RestoreAfter(request);
        pendingContext = null;
    }

    private static void HandlePickCancelled(AIScenePickRequest request)
    {
        RestoreAfter(request);
        pendingContext = null;
    }

    private static void RestoreAfter(AIScenePickRequest request)
    {
        AIScenePickFocusGuard.EndAll();

        if (request?.restoreState == null)
            return;

        AIScenePickRestoreState state = request.restoreState;

        EditorApplication.delayCall += () =>
        {
            SkyPrisonEditorWindow.RestoreAfterScenePick();

            if (state.windowType == "AI.LogicSentenceTemplatePicker")
                AILogicSentenceTemplatePickerWindow.ReopenFromRestoreState(state);
        };
    }

    private static void ApplyValueToRestoreStateWorkingInstance(
        AIScenePickRestoreState restoreState,
        string slotId,
        LogicSlotValue value)
    {
        if (restoreState == null || restoreState.workingInstance == null || string.IsNullOrWhiteSpace(slotId))
            return;

        LogicSentenceInstance instance = restoreState.workingInstance;
        LogicSlotAssignment target = null;

        for (int i = 0; i < instance.slotAssignments.Count; i++)
        {
            LogicSlotAssignment a = instance.slotAssignments[i];
            if (a != null && a.slotId == slotId)
            {
                target = a;
                break;
            }
        }

        if (target == null)
        {
            target = new LogicSlotAssignment
            {
                slotId = slotId,
                value = new LogicSlotValue()
            };
            instance.slotAssignments.Add(target);
        }

        target.value = CloneValueSafe(value);
    }

    private static LogicSlotValue CloneValueSafe(LogicSlotValue src)
    {
        if (src == null)
            return new LogicSlotValue();

        return new LogicSlotValue
        {
            valueType = src.valueType,
            sourceType = src.sourceType,
            boolValue = src.boolValue,
            intValue = src.intValue,
            floatValue = src.floatValue,
            stringValue = src.stringValue,
            enumValue = src.enumValue,
            variableKey = src.variableKey,
            contextKey = src.contextKey,
            assetReference = src.assetReference,
            sceneObjectId = src.sceneObjectId,
            sceneObjectName = src.sceneObjectName
        };
    }
}
