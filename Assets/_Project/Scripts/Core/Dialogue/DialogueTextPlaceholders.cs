using UnityEngine;

/// <summary>
/// 台词文本里的占位符替换——目前只有 {player}，替换成当前玩家角色的本地化名字。
/// 玩家角色本身固定是Axia（不是可自定义昵称的那种"玩家名"），但不直接把"Axia"
/// 写死在每句台词的文字里——从 UnitDefinition.localizedNames 读，跟着语言走，
/// 以后真要支持自定义/换角色也不用回头改台本文本。
/// </summary>
public static class DialogueTextPlaceholders
{
    private const string PlayerToken = "{player}";

    public static string Resolve(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(PlayerToken)) return text;
        return text.Replace(PlayerToken, ResolvePlayerName());
    }

    private static string ResolvePlayerName()
    {
        var playerUnit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (playerUnit == null || playerUnit.UnitObject == null) return "";

        var binder = playerUnit.UnitObject.GetComponent<UnitDefinitionRuntimeBinder>();
        UnitDefinition def = binder != null ? binder.UnitDefinitionAsset : null;
        if (def == null) return "";

        return LocalizationRuntime.Instance != null
            ? LocalizationRuntime.Instance.GetText(def.localizedNames, def.displayName)
            : def.displayName;
    }
}
