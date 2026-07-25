#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class SkyPrisonInputPromptIconDatabaseSeeder
{
    private const string KeyboardFolder = "Assets/_Project/UIUX/Source/Keyboard";
    private const string MouseFolder = "Assets/_Project/UIUX/Source/Mouse";
    private const string GamepadFolder = "Assets/_Project/UIUX/Source/Gamepad";
    private const string DatabasePath = SkyPrisonInputPromptIconDatabase.DefaultAssetPath;

    [MenuItem("Tools/Sky Prison/UI/Input Prompts/创建或更新按键图标数据库")]
    public static void CreateOrUpdateDatabase()
    {
        EnsureFolderExists(Path.GetDirectoryName(DatabasePath).Replace('\\', '/'));

        SkyPrisonInputPromptIconDatabase database = AssetDatabase.LoadAssetAtPath<SkyPrisonInputPromptIconDatabase>(DatabasePath);
        if (database == null)
        {
            database = ScriptableObject.CreateInstance<SkyPrisonInputPromptIconDatabase>();
            AssetDatabase.CreateAsset(database, DatabasePath);
        }

        int missingCount = 0;

        AddKeyboard(database, ref missingCount);
        AddMouse(database, ref missingCount);
        AddGamepad(database, ref missingCount);

        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[SkyPrisonInputPromptIconDatabaseSeeder] 更新完成: {DatabasePath}, entries={database.entries.Count}, missing={missingCount}");
        Selection.activeObject = database;
    }

    private static void AddKeyboard(SkyPrisonInputPromptIconDatabase db, ref int missing)
    {
        for (char c = 'A'; c <= 'Z'; c++)
            Add(db, ref missing, "keyboard/" + char.ToLowerInvariant(c), c.ToString(), KeyboardFolder, "Key_" + c);

        for (int i = 0; i <= 9; i++)
            Add(db, ref missing, "keyboard/" + i, i.ToString(), KeyboardFolder, "Key_" + i);

        Add(db, ref missing, "keyboard/space", "Space", KeyboardFolder, "Key_Space");
        Add(db, ref missing, "keyboard/shift", "Shift", KeyboardFolder, "Key_Shift");
        Add(db, ref missing, "keyboard/ctrl", "Ctrl", KeyboardFolder, "Key_Ctrl");
        Add(db, ref missing, "keyboard/alt", "Alt", KeyboardFolder, "Key_Alt");
        Add(db, ref missing, "keyboard/command", "Command", KeyboardFolder, "Key_Command");
        Add(db, ref missing, "keyboard/tab", "Tab", KeyboardFolder, "Key_Tab", "KeyTab");
        Add(db, ref missing, "keyboard/enter", "Enter", KeyboardFolder, "Key_Enter");
        Add(db, ref missing, "keyboard/esc", "Esc", KeyboardFolder, "Key_Esc");
        // 素材文件名之前拼错成 Key_Delect.png，已经改回正确拼写 Key_Delete.png。
        Add(db, ref missing, "keyboard/delete", "Delete", KeyboardFolder, "Key_Delete");

        Add(db, ref missing, "keyboard/up", "↑", KeyboardFolder, "Key_Up");
        Add(db, ref missing, "keyboard/down", "↓", KeyboardFolder, "Key_Down");
        Add(db, ref missing, "keyboard/left", "←", KeyboardFolder, "Key_Left");
        Add(db, ref missing, "keyboard/right", "→", KeyboardFolder, "Key_Right");

        for (int i = 1; i <= 12; i++)
            Add(db, ref missing, "keyboard/f" + i, "F" + i, KeyboardFolder, "Key_F" + i);

        Add(db, ref missing, "keyboard/home", "Home", KeyboardFolder, "Key_Home");
        Add(db, ref missing, "keyboard/end", "End", KeyboardFolder, "Key_End");
        Add(db, ref missing, "keyboard/page_up", "Page Up", KeyboardFolder, "Key_Page-Up", "Key_Page_Up");
        Add(db, ref missing, "keyboard/page_down", "Page Down", KeyboardFolder, "Key_Page-Down", "Key_Page_Down");
        Add(db, ref missing, "keyboard/ins", "Ins", KeyboardFolder, "Key_Ins");
        Add(db, ref missing, "keyboard/num_lock", "Num Lock", KeyboardFolder, "Key_Num-lock", "Key_Num_Lock");

        Add(db, ref missing, "keyboard/slash", "/", KeyboardFolder, "Key_Slash");
        Add(db, ref missing, "keyboard/backslash", "\\", KeyboardFolder, "Key_Backslash");
        Add(db, ref missing, "keyboard/plus", "+", KeyboardFolder, "Key_+");
        Add(db, ref missing, "keyboard/minus", "-", KeyboardFolder, "Key_-");
        Add(db, ref missing, "keyboard/equals", "=", KeyboardFolder, "Key_=");
        Add(db, ref missing, "keyboard/asterisk", "*", KeyboardFolder, "Key_Asterisk");
        Add(db, ref missing, "keyboard/colon", ":", KeyboardFolder, "Key_Colon", "Key_:");
        Add(db, ref missing, "keyboard/period", ".", KeyboardFolder, "Key_.", "Key_Period");
    }

    private static void AddMouse(SkyPrisonInputPromptIconDatabase db, ref int missing)
    {
        Add(db, ref missing, "mouse/left", "鼠标左键", MouseFolder, "Mouse_Left");
        Add(db, ref missing, "mouse/right", "鼠标右键", MouseFolder, "Mouse_Right");
        Add(db, ref missing, "mouse/scroll", "鼠标滚轮", MouseFolder, "Mouse_Scroll");
    }

    private static void AddGamepad(SkyPrisonInputPromptIconDatabase db, ref int missing)
    {
        Add(db, ref missing, "gamepad/xbox/a", "A", GamepadFolder, "Gamepad_A");
        Add(db, ref missing, "gamepad/xbox/b", "B", GamepadFolder, "Gamepad_B");
        Add(db, ref missing, "gamepad/xbox/x", "X", GamepadFolder, "Gamepad_X");
        Add(db, ref missing, "gamepad/xbox/y", "Y", GamepadFolder, "Gamepad_Y");

        Add(db, ref missing, "gamepad/playstation/cross", "×", GamepadFolder, "Gamepad_Cross");
        Add(db, ref missing, "gamepad/playstation/circle", "○", GamepadFolder, "Gamepad_Circle");
        Add(db, ref missing, "gamepad/playstation/square", "□", GamepadFolder, "Gamepad_Square");
        Add(db, ref missing, "gamepad/playstation/triangle", "△", GamepadFolder, "Gamepad_Triangle", "Gamepad_triangle");

        Add(db, ref missing, "gamepad/up", "↑", GamepadFolder, "Gamepad_Up");
        Add(db, ref missing, "gamepad/down", "↓", GamepadFolder, "Gamepad_Down");
        Add(db, ref missing, "gamepad/left", "←", GamepadFolder, "Gamepad_Left");
        Add(db, ref missing, "gamepad/right", "→", GamepadFolder, "Gamepad_Right");

        Add(db, ref missing, "gamepad/l", "L", GamepadFolder, "Gamepad_L");
        Add(db, ref missing, "gamepad/r", "R", GamepadFolder, "Gamepad_R");
        Add(db, ref missing, "gamepad/l1", "L1", GamepadFolder, "Gamepad_L1");
        Add(db, ref missing, "gamepad/r1", "R1", GamepadFolder, "Gamepad_R1");
        Add(db, ref missing, "gamepad/l2", "L2", GamepadFolder, "Gamepad_L2");
        Add(db, ref missing, "gamepad/r2", "R2", GamepadFolder, "Gamepad_R2");
        Add(db, ref missing, "gamepad/l3", "L3", GamepadFolder, "Gamepad_L3");
        Add(db, ref missing, "gamepad/r3", "R3", GamepadFolder, "Gamepad_R3");

        Add(db, ref missing, "gamepad/select", "Select", GamepadFolder, "Gamepad_Select");
        Add(db, ref missing, "gamepad/star", "Start", GamepadFolder, "Gamepad_Star", "Gamepad_Start");
        Add(db, ref missing, "gamepad/keycap", "Gamepad", GamepadFolder, "Gamepad_Keycap");

        // Xbox 专用图标（LB/RB/LT/RT/View/Menu 是 Xbox 命名，跟上面 L1/R1/L2/R2/Select/Star
        // 那套 PS 命名的图标分开，GamepadKeyCodeToIconKey 按 style 选用哪一套）。
        Add(db, ref missing, "gamepad/xbox/lb", "LB", GamepadFolder, "Gamepad_LB");
        Add(db, ref missing, "gamepad/xbox/rb", "RB", GamepadFolder, "Gamepad_RB");
        Add(db, ref missing, "gamepad/xbox/lt", "LT", GamepadFolder, "Gamepad_LT");
        Add(db, ref missing, "gamepad/xbox/rt", "RT", GamepadFolder, "Gamepad_RT");
        Add(db, ref missing, "gamepad/xbox/view", "View", GamepadFolder, "Gamepad_View");
        Add(db, ref missing, "gamepad/xbox/menu", "Menu", GamepadFolder, "Gamepad_Menu");
        // L3/R3（摇杆按下）在 Xbox 手柄上没有单独的圆圈数字图标，用现成的 Gamepad_L/Gamepad_R
        // （跟 "gamepad/l" "gamepad/r" 是同一张图，只是换个 key 给 Xbox 风格用）。
        Add(db, ref missing, "gamepad/xbox/l", "L3", GamepadFolder, "Gamepad_L");
        Add(db, ref missing, "gamepad/xbox/r", "R3", GamepadFolder, "Gamepad_R");
    }

    private static void Add(
        SkyPrisonInputPromptIconDatabase db,
        ref int missing,
        string iconKey,
        string displayName,
        string folder,
        params string[] assetNames)
    {
        Sprite sprite = FindSprite(folder, assetNames);
        if (sprite == null)
        {
            missing++;
            Debug.LogWarning($"[SkyPrisonInputPromptIconDatabaseSeeder] 找不到图标: {iconKey} in {folder} names={string.Join(",", assetNames)}");
        }

        db.SetOrUpdateEntry(iconKey, displayName, sprite);
    }

    private static Sprite FindSprite(string folder, params string[] assetNames)
    {
        if (string.IsNullOrWhiteSpace(folder) || assetNames == null || assetNames.Length == 0)
            return null;

        for (int i = 0; i < assetNames.Length; i++)
        {
            string assetName = assetNames[i];
            if (string.IsNullOrWhiteSpace(assetName))
                continue;

            string[] guids = AssetDatabase.FindAssets(assetName, new[] { folder });
            for (int g = 0; g < guids.Length; g++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[g]);
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.Equals(fileName, assetName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null)
                    return sprite;

                Object[] all = AssetDatabase.LoadAllAssetsAtPath(path);
                Sprite nested = all.OfType<Sprite>().FirstOrDefault();
                if (nested != null)
                    return nested;
            }
        }

        return null;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif
