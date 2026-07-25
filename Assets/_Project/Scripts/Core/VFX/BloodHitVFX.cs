using UnityEngine;

/// <summary>
/// 受击血液飞溅触发器。
/// 从 RVFX BloodEffectsPack URP 飞溅预制体中随机挑一个，在受击点生成并自动销毁。
/// 调用：BloodHitVFX.Spawn(hitWorldPos, attackerWorldPos);
/// </summary>
public static class BloodHitVFX
{
    // URP 飞溅预制体路径（相对 Assets/）
    private static readonly string[] SplashPrefabPaths = new[]
    {
        "RVFX/BloodEffectsPack/1_URP/Blood/Splash/Blood_Splash_01_URP",
        "RVFX/BloodEffectsPack/1_URP/Blood/Splash/Blood_Splash_02_URP",
        "RVFX/BloodEffectsPack/1_URP/Blood/Splash/Blood_Splash_03_URP",
        "RVFX/BloodEffectsPack/1_URP/Blood/Splash/Blood_Splash_04_URP",
        "RVFX/BloodEffectsPack/1_URP/Blood/Splash/Blood_Splash_05_URP",
    };

    private static GameObject[] _cachedPrefabs;
    private static bool _loaded;

    /// <summary>在受击位置生成随机飞溅效果，朝攻击来向的反方向喷出。</summary>
    public static void Spawn(Vector3 hitPosition, Vector3 attackerPosition)
    {
        EnsureLoaded();

        if (_cachedPrefabs == null || _cachedPrefabs.Length == 0)
            return;

        GameObject prefab = _cachedPrefabs[Random.Range(0, _cachedPrefabs.Length)];
        if (prefab == null)
            return;

        // 旋转：让特效朝攻击反方向（受击者飞出的方向）
        Vector3 dir = hitPosition - attackerPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) dir = Vector3.right;
        Quaternion rot = Quaternion.LookRotation(dir.normalized, Vector3.up);

        GameObject go = Object.Instantiate(prefab, hitPosition, rot);

        // KillEffect 组件会自动销毁，没有的话我们兜底销毁
        Object.Destroy(go, 4f);
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        _cachedPrefabs = new GameObject[SplashPrefabPaths.Length];
        for (int i = 0; i < SplashPrefabPaths.Length; i++)
        {
            _cachedPrefabs[i] = Resources.Load<GameObject>(SplashPrefabPaths[i]);
        }
    }
}
