using UnityEngine;

/// <summary>
/// 物品材质音效表。每种 ItemSoundMaterial 配置拿起/放下两组 Clip，运行时随机取一个播放。
/// 路径：Resources/Data/Audio/SkyPrisonItemMaterialSoundTable
/// </summary>
[CreateAssetMenu(menuName = "Sky Prison/Audio/物品材质音效表", fileName = "SkyPrisonItemMaterialSoundTable")]
public class SkyPrisonItemMaterialSoundTable : ScriptableObject
{
    public const string ResourcesPath = "Data/Audio/SkyPrisonItemMaterialSoundTable";

    [System.Serializable]
    public class Entry
    {
        public ItemSoundMaterial material;
        [Tooltip("拿起音效（随机取一个）")]
        public AudioClip[] pickupClips;
        [Tooltip("放下/落点音效（随机取一个）")]
        public AudioClip[] dropClips;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Range(0.5f, 2f)] public float pitch = 1f;
        [Range(0f, 0.3f)] public float pitchVariance = 0.05f;
    }

    [Tooltip("所有物品拾取/放下音效的全局音量倍率")]
    [Range(0f, 1f)] public float globalVolumeMultiplier = 0.8f;

    public Entry[] entries;

    private Entry[] _indexed;

    private void OnEnable() => _indexed = null;

    public bool TryGet(ItemSoundMaterial material, out Entry entry)
    {
        if (_indexed == null) BuildIndex();
        int i = (int)material;
        if (i >= 0 && i < _indexed.Length && _indexed[i] != null)
        { entry = _indexed[i]; return true; }
        // 回退到 Default
        if (_indexed.Length > 0 && _indexed[0] != null)
        { entry = _indexed[0]; return true; }
        entry = null; return false;
    }

    private void BuildIndex()
    {
        int max = System.Enum.GetValues(typeof(ItemSoundMaterial)).Length;
        _indexed = new Entry[max];
        if (entries == null) return;
        foreach (var e in entries)
        {
            int i = (int)e.material;
            if (i >= 0 && i < max) _indexed[i] = e;
        }
    }

    private static SkyPrisonItemMaterialSoundTable _instance;
    public static SkyPrisonItemMaterialSoundTable Instance
    {
        get
        {
            if (_instance == null)
                _instance = Resources.Load<SkyPrisonItemMaterialSoundTable>(ResourcesPath);
            return _instance;
        }
    }

    public static void PlayPickup(ItemSoundMaterial material)  => Play(material, pickup: true);
    public static void PlayDrop(ItemSoundMaterial material)    => Play(material, pickup: false);

    private static void Play(ItemSoundMaterial material, bool pickup)
    {
        var table = Instance;
        if (table == null || !table.TryGet(material, out var e)) return;
        var clips = pickup ? e.pickupClips : e.dropClips;
        if (clips == null || clips.Length == 0) return;

        var clip = clips[Random.Range(0, clips.Length)];
        if (clip == null) return;

        var gs = SkyPrisonAudioGlobalSettings.Instance;
        float vol = e.volume * table.globalVolumeMultiplier * (gs != null ? gs.seVolume * gs.masterVolume : 1f);
        float pt  = e.pitch + Random.Range(-e.pitchVariance, e.pitchVariance);

        // 借用 SystemSEPlayer 的 AudioSource（2D，一次性）
        SkyPrisonSystemSEPlayer.PlayClip(clip, vol, pt);
    }
}
