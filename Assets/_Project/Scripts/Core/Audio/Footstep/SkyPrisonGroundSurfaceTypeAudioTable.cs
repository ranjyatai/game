using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 地面标签（GroundSurfaceType）→ 脚步声音效包。全项目一份，放在 Resources 里。
///
/// 地表材质没单独挂 surfaceAudioPackage 时，脚步声按它的地面标签来这里查——同一种标签的
/// 材质（各种水泥、柏油、地砖）自然共用一套脚步声，不用每个材质都去挂一次。材质自己挂了
/// 音效包的仍然优先，用于「这种金属声音要不一样」这类特例。
///
/// 查表时的运行层 Key 用表里这一行的 runtimeLayerKey，而不是材质自己的 audioRuntimeLayerKey：
/// 材质的 Key 是给它自己的音效包配的（比如水泥材质填的是 footstep_default），拿去配标签的
/// 音效包会对不上音轨，结果还是没声音。表里 Key 留空才退回材质自己的。
/// </summary>
[CreateAssetMenu(
    fileName = ResourcesAssetName,
    menuName = "Sky Prison/Audio/Ground Surface Type Audio Table",
    order = 1320)]
public sealed class SkyPrisonGroundSurfaceTypeAudioTable : ScriptableObject
{
    public const string ResourcesAssetName = "SkyPrisonGroundSurfaceTypeAudioTable";

    [Serializable]
    public sealed class Entry
    {
        public GroundSurfaceType surfaceType = GroundSurfaceType.Default;
        public SkyPrisonAudioPackage audioPackage;
        [Tooltip("音效包内部的运行层 Key（如 surface_metal）。留空则用材质自己的 audioRuntimeLayerKey。")]
        public string runtimeLayerKey = "";
    }

    public List<Entry> entries = new List<Entry>();

    private static SkyPrisonGroundSurfaceTypeAudioTable cachedInstance;
    private static bool loadAttempted;

    public static SkyPrisonGroundSurfaceTypeAudioTable Instance
    {
        get
        {
            if (cachedInstance == null && !loadAttempted)
            {
                loadAttempted = true;
                cachedInstance = Resources.Load<SkyPrisonGroundSurfaceTypeAudioTable>(ResourcesAssetName);
            }
            return cachedInstance;
        }
    }

    public bool TryGet(GroundSurfaceType type, out SkyPrisonAudioPackage package, out string runtimeLayerKey)
    {
        package = null;
        runtimeLayerKey = "";
        if (entries == null)
            return false;

        for (int i = 0; i < entries.Count; i++)
        {
            Entry e = entries[i];
            if (e == null || e.surfaceType != type || e.audioPackage == null)
                continue;

            package = e.audioPackage;
            runtimeLayerKey = e.runtimeLayerKey ?? "";
            return true;
        }

        return false;
    }

#if UNITY_EDITOR
    // 编辑器里改了表（或新建了表）后，让下次查询重新加载。
    private void OnValidate()
    {
        cachedInstance = null;
        loadAttempted = false;
    }
#endif
}
