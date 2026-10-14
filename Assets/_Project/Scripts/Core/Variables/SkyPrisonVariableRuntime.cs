using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 自定义变量的运行时读写入口——LogicSlotValue.EvaluateXxx() 遇到
/// sourceType=Variable 时就调这里。Bool/Int/Float/String 直接读写存档
/// (SaveManager.Player)，永久持久化；Unit/Item 只在当前场景会话有效，存在
/// 静态字典里，切场景就清空——绑定的是具体的场景GameObject/单次引用，场景一卸载
/// 那个实例本来就没了，硬存进存档也读不回来。
/// </summary>
public static class SkyPrisonVariableRuntime
{
    private static readonly Dictionary<string, GameObject> unitVariables = new Dictionary<string, GameObject>();
    private static readonly Dictionary<string, ItemDefinition> itemVariables = new Dictionary<string, ItemDefinition>();

    private static bool sceneLoadedSubscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        unitVariables.Clear();
        itemVariables.Clear();
        sceneLoadedSubscribed = false;
    }

    private static void EnsureSceneLoadedSubscribed()
    {
        if (sceneLoadedSubscribed)
            return;
        SceneManager.sceneLoaded += HandleSceneLoaded;
        sceneLoadedSubscribed = true;
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        unitVariables.Clear();
        itemVariables.Clear();
    }

    // ── Bool ─────────────────────────────────────────────────────────────────

    public static bool GetBool(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return false;
        return SaveManager.Player.boolVariables.TryGetValue(name, out bool value) && value;
    }

    public static void SetBool(string name, bool value)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return;
        SaveManager.Player.boolVariables[name] = value;
    }

    // ── Int ──────────────────────────────────────────────────────────────────

    public static int GetInt(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return 0;
        return SaveManager.Player.intVariables.TryGetValue(name, out int value) ? value : 0;
    }

    public static void SetInt(string name, int value)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return;
        SaveManager.Player.intVariables[name] = value;
    }

    // ── Float ────────────────────────────────────────────────────────────────

    public static float GetFloat(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return 0f;
        return SaveManager.Player.floatVariables.TryGetValue(name, out float value) ? value : 0f;
    }

    public static void SetFloat(string name, float value)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return;
        SaveManager.Player.floatVariables[name] = value;
    }

    // ── String ───────────────────────────────────────────────────────────────

    public static string GetString(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return "";
        return SaveManager.Player.stringVariables.TryGetValue(name, out string value) ? value : "";
    }

    public static void SetString(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(name) || SaveManager.Player == null)
            return;
        SaveManager.Player.stringVariables[name] = value ?? "";
    }

    // ── Unit（会话内有效，不写存档）──────────────────────────────────────────

    public static GameObject GetUnit(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return unitVariables.TryGetValue(name, out GameObject value) ? value : null;
    }

    public static void SetUnit(string name, GameObject value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        EnsureSceneLoadedSubscribed();
        unitVariables[name] = value;
    }

    // ── Item（会话内有效，不写存档）──────────────────────────────────────────

    public static ItemDefinition GetItem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return itemVariables.TryGetValue(name, out ItemDefinition value) ? value : null;
    }

    public static void SetItem(string name, ItemDefinition value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        EnsureSceneLoadedSubscribed();
        itemVariables[name] = value;
    }
}
