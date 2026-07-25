using UnityEngine;

public static class SkyPrisonInputPromptResolver
{
    public static bool HasConnectedGamepad()
    {
        string[] names = Input.GetJoystickNames();
        if (names == null)
            return false;

        for (int i = 0; i < names.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(names[i]))
                return true;
        }

        return false;
    }

    public static SkyPrisonInputPromptDeviceStyle GetPreferredDeviceStyle(
        bool preferGamepadWhenConnected,
        SkyPrisonInputPromptDeviceStyle gamepadStyle = SkyPrisonInputPromptDeviceStyle.GamepadXbox)
    {
        if (preferGamepadWhenConnected && HasConnectedGamepad())
            return gamepadStyle;

        return SkyPrisonInputPromptDeviceStyle.KeyboardMouse;
    }

    public static bool TryResolveActionIcon(
        SkyPrisonInputSettings inputSettings,
        SkyPrisonInputPromptIconDatabase iconDatabase,
        SkyPrisonInputAction action,
        bool preferGamepadWhenConnected,
        SkyPrisonInputPromptDeviceStyle gamepadStyle,
        out Sprite sprite,
        out string iconKey,
        out KeyCode resolvedKey)
    {
        sprite = null;
        iconKey = null;
        resolvedKey = KeyCode.None;

        if (inputSettings == null || iconDatabase == null)
            return false;

        SkyPrisonInputBinding binding = inputSettings.GetBinding(action);
        if (binding == null)
            return false;

        SkyPrisonInputPromptDeviceStyle style = GetPreferredDeviceStyle(preferGamepadWhenConnected, gamepadStyle);
        bool preferGamepad = SkyPrisonInputPromptIconDatabase.IsGamepadStyle(style);

        return iconDatabase.TryGetSpriteForBinding(
            binding,
            style,
            preferGamepad,
            out sprite,
            out iconKey,
            out resolvedKey);
    }
}
