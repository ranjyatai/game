using System.Collections.Generic;

public static class LogicSlotHandlerRegistry
{
    private static readonly Dictionary<LogicSlotValueType, ILogicSlotHandler> handlers = new();

    static LogicSlotHandlerRegistry()
    {
        Register(new LogicBoolSlotHandler());
        Register(new LogicIntSlotHandler());
        Register(new LogicFloatSlotHandler());
        Register(new LogicPercentSlotHandler());
        Register(new LogicStringSlotHandler());
        Register(new LogicEnumSlotHandler());
        Register(new LogicComparisonOperatorSlotHandler());
        Register(new LogicUnitSlotHandler());
        Register(new LogicAIPackageSlotHandler());
        Register(new LogicItemSlotHandler());
        Register(new LogicCurrencySlotHandler());
        Register(new LogicAudioClipSlotHandler());
        Register(new LogicDialogueLibrarySlotHandler());
        Register(new LogicAreaSlotHandler());
        Register(new LogicShopSlotHandler());
        Register(new LogicQuestSlotHandler());
        Register(new LogicUnitAssetSlotHandler());
    }

    public static void Register(ILogicSlotHandler handler)
    {
        if (handler == null) return;
        handlers[handler.ValueType] = handler;
    }

    public static ILogicSlotHandler Get(LogicSlotValueType valueType)
    {
        handlers.TryGetValue(valueType, out ILogicSlotHandler handler);
        return handler;
    }
}
