using System;
using System.Collections.Generic;
using UnityEngine;

public enum SkyPrisonInputPromptDeviceStyle
{
    KeyboardMouse = 0,
    GamepadGeneric = 1,
    GamepadXbox = 2,
    GamepadPlayStation = 3,
}

[Serializable]
public class SkyPrisonInputPromptIconEntry
{
    public string iconKey = "keyboard/a";
    public string displayName = "A";
    public Sprite icon;
}

[CreateAssetMenu(
    fileName = "InputPromptIconDatabase",
    menuName = "Sky Prison/UI/Input Prompt Icon Database",
    order = 1700)]
public class SkyPrisonInputPromptIconDatabase : ScriptableObject
{
    public const string DefaultAssetPath = "Assets/_Project/UIUX/Data/InputPromptIconDatabase.asset";

    [Header("Icon Entries")]
    public List<SkyPrisonInputPromptIconEntry> entries = new List<SkyPrisonInputPromptIconEntry>();

    private Dictionary<string, SkyPrisonInputPromptIconEntry> lookup;

    public void RebuildLookup()
    {
        if (lookup == null)
            lookup = new Dictionary<string, SkyPrisonInputPromptIconEntry>();
        else
            lookup.Clear();

        if (entries == null)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            SkyPrisonInputPromptIconEntry entry = entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.iconKey))
                continue;

            lookup[NormalizeIconKey(entry.iconKey)] = entry;
        }
    }

    public bool TryGetEntry(string iconKey, out SkyPrisonInputPromptIconEntry entry)
    {
        entry = null;

        if (string.IsNullOrWhiteSpace(iconKey))
            return false;

        if (lookup == null || lookup.Count == 0)
            RebuildLookup();

        return lookup != null && lookup.TryGetValue(NormalizeIconKey(iconKey), out entry) && entry != null;
    }

    public bool TryGetSprite(string iconKey, out Sprite sprite)
    {
        sprite = null;

        SkyPrisonInputPromptIconEntry entry;
        if (!TryGetEntry(iconKey, out entry))
            return false;

        sprite = entry.icon;
        return sprite != null;
    }

    public Sprite GetSprite(string iconKey)
    {
        Sprite sprite;
        TryGetSprite(iconKey, out sprite);
        return sprite;
    }

    public bool TryGetSpriteForKeyCode(KeyCode key, SkyPrisonInputPromptDeviceStyle style, out Sprite sprite, out string iconKey)
    {
        sprite = null;
        iconKey = KeyCodeToIconKey(key, style);

        if (string.IsNullOrWhiteSpace(iconKey))
            return false;

        return TryGetSprite(iconKey, out sprite);
    }

    public bool TryGetSpriteForBinding(
        SkyPrisonInputBinding binding,
        SkyPrisonInputPromptDeviceStyle preferredStyle,
        bool preferGamepadWhenAvailable,
        out Sprite sprite,
        out string iconKey,
        out KeyCode resolvedKey)
    {
        sprite = null;
        iconKey = null;
        resolvedKey = KeyCode.None;

        if (binding == null)
            return false;

        if (preferGamepadWhenAvailable && IsGamepadStyle(preferredStyle) && binding.gamepadKey != KeyCode.None)
        {
            if (TryGetSpriteForKeyCode(binding.gamepadKey, preferredStyle, out sprite, out iconKey))
            {
                resolvedKey = binding.gamepadKey;
                return true;
            }
        }

        if (binding.primaryKey != KeyCode.None)
        {
            SkyPrisonInputPromptDeviceStyle primaryStyle = IsMouseKey(binding.primaryKey)
                ? SkyPrisonInputPromptDeviceStyle.KeyboardMouse
                : SkyPrisonInputPromptDeviceStyle.KeyboardMouse;

            if (TryGetSpriteForKeyCode(binding.primaryKey, primaryStyle, out sprite, out iconKey))
            {
                resolvedKey = binding.primaryKey;
                return true;
            }
        }

        if (binding.secondaryKey != KeyCode.None)
        {
            if (TryGetSpriteForKeyCode(binding.secondaryKey, SkyPrisonInputPromptDeviceStyle.KeyboardMouse, out sprite, out iconKey))
            {
                resolvedKey = binding.secondaryKey;
                return true;
            }
        }

        if (binding.gamepadKey != KeyCode.None)
        {
            if (TryGetSpriteForKeyCode(binding.gamepadKey, preferredStyle, out sprite, out iconKey))
            {
                resolvedKey = binding.gamepadKey;
                return true;
            }
        }

        return false;
    }

    public static string KeyCodeToIconKey(KeyCode key, SkyPrisonInputPromptDeviceStyle style)
    {
        if (key == KeyCode.None)
            return null;

        string mouseKey = MouseKeyCodeToIconKey(key);
        if (!string.IsNullOrEmpty(mouseKey))
            return mouseKey;

        string gamepadKey = GamepadKeyCodeToIconKey(key, style);
        if (!string.IsNullOrEmpty(gamepadKey))
            return gamepadKey;

        return KeyboardKeyCodeToIconKey(key);
    }

    public static bool IsGamepadStyle(SkyPrisonInputPromptDeviceStyle style)
    {
        return style == SkyPrisonInputPromptDeviceStyle.GamepadGeneric
            || style == SkyPrisonInputPromptDeviceStyle.GamepadXbox
            || style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation;
    }

    public static bool IsMouseKey(KeyCode key)
    {
        return key == KeyCode.Mouse0 || key == KeyCode.Mouse1 || key == KeyCode.Mouse2;
    }

    public static bool IsGamepadKey(KeyCode key)
    {
        return key >= KeyCode.JoystickButton0 && key <= KeyCode.JoystickButton19;
    }

    public static string NormalizeIconKey(string key)
    {
        return string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim().ToLowerInvariant().Replace('\\', '/');
    }

#if UNITY_EDITOR
    public void SetOrUpdateEntry(string iconKey, string displayName, Sprite icon)
    {
        if (entries == null)
            entries = new List<SkyPrisonInputPromptIconEntry>();

        string normalized = NormalizeIconKey(iconKey);
        SkyPrisonInputPromptIconEntry entry = null;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null && NormalizeIconKey(entries[i].iconKey) == normalized)
            {
                entry = entries[i];
                break;
            }
        }

        if (entry == null)
        {
            entry = new SkyPrisonInputPromptIconEntry();
            entries.Add(entry);
        }

        entry.iconKey = normalized;
        entry.displayName = displayName;
        entry.icon = icon;
        RebuildLookup();
    }
#endif

    private static string MouseKeyCodeToIconKey(KeyCode key)
    {
        switch (key)
        {
            case KeyCode.Mouse0: return "mouse/left";
            case KeyCode.Mouse1: return "mouse/right";
            case KeyCode.Mouse2: return "mouse/scroll";
            default: return null;
        }
    }

    private static string GamepadKeyCodeToIconKey(KeyCode key, SkyPrisonInputPromptDeviceStyle style)
    {
        if (!IsGamepadKey(key))
            return null;

        int index = (int)key - (int)KeyCode.JoystickButton0;

        switch (index)
        {
            case 0:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/playstation/cross";
                return "gamepad/xbox/a";
            case 1:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/playstation/circle";
                return "gamepad/xbox/b";
            case 2:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/playstation/square";
                return "gamepad/xbox/x";
            case 3:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/playstation/triangle";
                return "gamepad/xbox/y";
            case 4:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/l1";
                return "gamepad/xbox/lb";
            case 5:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/r1";
                return "gamepad/xbox/rb";
            case 6:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/select";
                return "gamepad/xbox/view";
            case 7:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/star";
                return "gamepad/xbox/menu";
            case 8:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/l3";
                return "gamepad/xbox/l";
            case 9:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/r3";
                return "gamepad/xbox/r";
            case 10:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/l2";
                return "gamepad/xbox/lt";
            case 11:
                if (style == SkyPrisonInputPromptDeviceStyle.GamepadPlayStation) return "gamepad/r2";
                return "gamepad/xbox/rt";
            case 12: return "gamepad/up";
            case 13: return "gamepad/down";
            case 14: return "gamepad/left";
            case 15: return "gamepad/right";
            default: return "gamepad/button" + index;
        }
    }

    private static string KeyboardKeyCodeToIconKey(KeyCode key)
    {
        if (key >= KeyCode.A && key <= KeyCode.Z)
        {
            char c = (char)('a' + ((int)key - (int)KeyCode.A));
            return "keyboard/" + c;
        }

        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
        {
            int n = (int)key - (int)KeyCode.Alpha0;
            return "keyboard/" + n;
        }

        if (key >= KeyCode.F1 && key <= KeyCode.F12)
        {
            int f = 1 + ((int)key - (int)KeyCode.F1);
            return "keyboard/f" + f;
        }

        switch (key)
        {
            case KeyCode.Space: return "keyboard/space";
            case KeyCode.LeftShift:
            case KeyCode.RightShift: return "keyboard/shift";
            case KeyCode.LeftControl:
            case KeyCode.RightControl: return "keyboard/ctrl";
            case KeyCode.LeftAlt:
            case KeyCode.RightAlt: return "keyboard/alt";
            case KeyCode.LeftCommand:
            case KeyCode.RightCommand: return "keyboard/command";
            case KeyCode.Tab: return "keyboard/tab";
            case KeyCode.Return:
            case KeyCode.KeypadEnter: return "keyboard/enter";
            case KeyCode.Escape: return "keyboard/esc";
            case KeyCode.Delete:
            case KeyCode.Backspace: return "keyboard/delete";
            case KeyCode.UpArrow: return "keyboard/up";
            case KeyCode.DownArrow: return "keyboard/down";
            case KeyCode.LeftArrow: return "keyboard/left";
            case KeyCode.RightArrow: return "keyboard/right";
            case KeyCode.Home: return "keyboard/home";
            case KeyCode.End: return "keyboard/end";
            case KeyCode.PageUp: return "keyboard/page_up";
            case KeyCode.PageDown: return "keyboard/page_down";
            case KeyCode.Insert: return "keyboard/ins";
            case KeyCode.Numlock: return "keyboard/num_lock";
            case KeyCode.Slash: return "keyboard/slash";
            case KeyCode.Backslash: return "keyboard/backslash";
            case KeyCode.Minus:
            case KeyCode.KeypadMinus: return "keyboard/minus";
            case KeyCode.Equals: return "keyboard/equals";
            case KeyCode.KeypadPlus: return "keyboard/plus";
            case KeyCode.KeypadMultiply: return "keyboard/asterisk";
            case KeyCode.Colon:
            case KeyCode.Semicolon: return "keyboard/colon";
            case KeyCode.Period: return "keyboard/period";
            default: return null;
        }
    }
}
