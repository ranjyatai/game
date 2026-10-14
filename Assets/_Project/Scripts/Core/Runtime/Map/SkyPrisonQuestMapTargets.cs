using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战术地图上的「任务目标」：把进行中任务的当前目标(第一个没完成的)里指向的 NPC / 地点 / 区域，解析成这张地图上的世界位置。
/// 目标的完成条件用的是触发器条件语言，没有单独的「位置」字段，所以这里按条件类型读它指向的东西：
///   · cond_has_talked_to_npc(unit) / cond_has_delivered_item_to_npc(npc) → 场景里这个 NPC 当前的位置(会走动，返回它的 Transform)
///   · cond_player_near_scene_object(target) → 场景单位标记的位置
///   · cond_player_in_region(region，expected=true) → 区域标记的位置
/// 目标不在当前这张地图上(NPC / 标记在别的场景)就解析不到，不显示。不依赖玩家有没有走到过：任务指出的地方一接任务就标出来。
/// </summary>
public static class SkyPrisonQuestMapTargets
{
    public struct Target
    {
        public Vector3 position;
        public Transform follow;          // 会走动的目标(NPC)
        public string label;              // 地图上写在标记旁边的字：任务名
    }

    public static void Collect(List<Target> into)
    {
        into.Clear();
        var runtime = FindRuntime();
        if (runtime == null) return;

        foreach (var quest in runtime.GetActiveQuests())
        {
            if (quest == null) continue;
            QuestObjective current = quest.GetCurrentObjective();
            if (current == null) continue;
            string title = quest.GetLocalizedTitle(quest.questId);
            foreach (var c in current.completionConditions) CollectFrom(c, title, into);
        }
    }

    static QuestRuntime FindRuntime()
    {
        return Object.FindFirstObjectByType<QuestRuntime>();
    }

    static void CollectFrom(LogicSentenceInstance s, string title, List<Target> into)
    {
        if (s == null || !s.enabled) return;
        switch (s.templateId)
        {
            case "cond_has_talked_to_npc": AddNpc(s.GetAssignment("unit")?.value?.assetReference as UnitDefinition, title, into); break;
            case "cond_has_delivered_item_to_npc": AddNpc(s.GetAssignment("npc")?.value?.assetReference as UnitDefinition, title, into); break;
            case "cond_player_near_scene_object":
            {
                string id = s.GetAssignment("target")?.value?.sceneObjectId;
                if (string.IsNullOrWhiteSpace(id)) break;
                foreach (var m in Object.FindObjectsByType<SkyPrisonSceneUnitMarker>(FindObjectsSortMode.None))
                    if (m.UnitDefinitionId == id || m.SceneUnitGuid == id) { into.Add(new Target { position = m.transform.position, follow = null, label = title }); break; }
                break;
            }
            case "cond_player_in_region":
            {
                var ev = s.GetAssignment("expected")?.value;
                if (ev != null && !ev.EvaluateBool()) break;      // 「不在区域内」不是要去的地方
                string id = s.GetAssignment("region")?.value?.sceneObjectId;
                if (string.IsNullOrWhiteSpace(id)) break;
                foreach (var r in Object.FindObjectsByType<SkyPrisonSceneRegionMarker>(FindObjectsSortMode.None))
                    if (r.SceneRegionGuid == id) { into.Add(new Target { position = r.transform.position, follow = null, label = title }); break; }
                break;
            }
        }
        if (s.conditionChildren != null) foreach (var ch in s.conditionChildren) CollectFrom(ch, title, into);
    }

    static void AddNpc(UnitDefinition def, string title, List<Target> into)
    {
        if (def == null) return;
        foreach (var b in Object.FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsSortMode.None))
        {
            if (b.UnitDefinitionAsset != def || !b.gameObject.activeInHierarchy) continue;
            into.Add(new Target { position = b.transform.position, follow = b.transform, label = title });
            return;
        }
    }
}
