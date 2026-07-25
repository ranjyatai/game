using System;
using System.Collections.Generic;
using UnityEngine;

public class CharacterStatJsonTest : MonoBehaviour
{
    public enum Language
    {
        CN,
        JP,
        EN
    }

    [Header("拖入 JSON 文件")]
    public TextAsset characterStatMasterJson;
    public TextAsset characterStatTextJson;

    [Header("测试参数")]
    public string testStateKey = "max_hp";
    public Language displayLanguage = Language.CN;

    [ContextMenu("Test Show Character Stat Info")]
    public void TestShowCharacterStatInfo()
    {
        if (characterStatMasterJson == null)
        {
            Debug.LogError("characterStatMasterJson 没有指定。");
            return;
        }

        if (characterStatTextJson == null)
        {
            Debug.LogError("characterStatTextJson 没有指定。");
            return;
        }

        CharacterStatMasterRoot masterData;
        CharacterStatTextRoot textData;

        try
        {
            masterData = JsonUtility.FromJson<CharacterStatMasterRoot>(characterStatMasterJson.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"character_stat_master.json 解析失败: {e.Message}");
            return;
        }

        try
        {
            textData = JsonUtility.FromJson<CharacterStatTextRoot>(characterStatTextJson.text);
        }
        catch (Exception e)
        {
            Debug.LogError($"character_stat_text.json 解析失败: {e.Message}");
            return;
        }

        if (masterData == null || masterData.stats == null || masterData.stats.Count == 0)
        {
            Debug.LogWarning("character_stat_master.json 没有读取到任何属性数据。");
            return;
        }

        CharacterStatData foundStat = masterData.stats.Find(stat => stat.stateKey == testStateKey);

        if (foundStat == null)
        {
            Debug.LogWarning($"没有找到 stateKey = {testStateKey} 的属性。");
            return;
        }

        string statName = GetLocalizedText(textData, displayLanguage, foundStat.nameKey);
        string statDesc = GetLocalizedText(textData, displayLanguage, foundStat.descKey);

        string langLabel = displayLanguage.ToString();

        Debug.Log(
            $"===== 角色属性信息 [{langLabel}] =====\n" +
            $"stateKey    : {foundStat.stateKey}\n" +
            $"varName     : {foundStat.varName}\n" +
            $"nameKey     : {foundStat.nameKey}\n" +
            $"descKey     : {foundStat.descKey}\n" +
            $"name        : {statName}\n" +
            $"description : {statDesc}\n" +
            $"baseValue   : {foundStat.baseValue}\n" +
            $"baseRate    : {foundStat.baseRate}\n" +
            $"=============================="
        );
    }

    private string GetLocalizedText(CharacterStatTextRoot textRoot, Language lang, string key)
    {
        if (string.IsNullOrEmpty(key))
            return "(key为空)";

        StatTextEntryList targetList = null;

        switch (lang)
        {
            case Language.CN:
                targetList = textRoot.cn;
                break;
            case Language.JP:
                targetList = textRoot.jp;
                break;
            case Language.EN:
                targetList = textRoot.en;
                break;
        }

        if (targetList == null || targetList.entries == null)
            return $"(未找到语言数据: {key})";

        StatTextEntry entry = targetList.entries.Find(e => e.key == key);

        if (entry == null)
            return $"(未找到文本: {key})";

        return entry.value;
    }
}

[Serializable]
public class CharacterStatMasterRoot
{
    public int version;
    public List<CharacterStatData> stats;
}

[Serializable]
public class CharacterStatData
{
    public string stateKey;
    public string varName;
    public string nameKey;
    public string descKey;
    public float baseValue;
    public float baseRate;
}

[Serializable]
public class CharacterStatTextRoot
{
    public StatTextEntryList cn;
    public StatTextEntryList jp;
    public StatTextEntryList en;
}

[Serializable]
public class StatTextEntryList
{
    public List<StatTextEntry> entries;
}

[Serializable]
public class StatTextEntry
{
    public string key;
    public string value;
}