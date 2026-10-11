using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 浮游炮(第一版)：AXIA 的辅助火力，自动瞄准，玩家不用操作。设计见 memory 的 project-floating-drones-design。
///   · AFP 里买「浮游炮数量」(1~4)，每个炮悬在 AXIA 肩上方；没买(0)就没有炮。科技树入口节点 strategist_drones 点亮才出现这几行(测试阶段 DebugForceUnlock 默认放开)。
///   · 每个炮各自弹仓(基础 6 发，「弹仓」每级 +2)，自动打最近的、没被墙挡住的敌人；打空后各自独立冷却(基础 6 秒，「冷却」每级 -10%，最低 40%)，冷却完满弹。
///   · 武器是脉冲激光：瞬时判定，会被墙挡住(没视线不开火)。单发 = 玩家攻击力 × 20% × 物理攻击强化倍率，不吃抗性。
///   · 一局内有效：AFP 等级清零后炮自己消失。第一版不做：诱导弹种、HUD 小点、音效、属性色。
/// </summary>
public static class SkyPrisonAfpDrones
{
    public const string NodeId = "strategist_drones";
    public const string CountKey = "drone_count", MagKey = "drone_mag", CdKey = "drone_cd";
    public const int BaseMagazine = 12, MagPerLevel = 3;
    public const float BaseCooldown = 4.5f, CooldownCutPerLevel = 0.10f, MinCooldownRatio = 0.4f;
    public const float DamageRatio = 0.20f, FireInterval = 0.40f, Range = 11f;

    public static bool NodeOn => SkyPrisonAfpAbsorbShield.NodeUnlocked(NodeId);

    static int LevelOf(string key)
    {
        var all = SkyPrisonAfpUpgrades.All;
        for (int i = 0; i < all.Length; i++) if (all[i].key == key) return SkyPrisonAfpUpgrades.Level(all[i]);
        return 0;
    }

    public static int Count => NodeOn ? Mathf.Clamp(LevelOf(CountKey), 0, 4) : 0;
    public static int Magazine => BaseMagazine + MagPerLevel * LevelOf(MagKey);
    public static float Cooldown => BaseCooldown * Mathf.Max(MinCooldownRatio, 1f - CooldownCutPerLevel * LevelOf(CdKey));
}

public sealed class SkyPrisonFloatingDrones : MonoBehaviour
{
    sealed class Drone
    {
        public Transform tr; public Renderer rend; public Material mat; public LineRenderer beam;
        public int shots; public float cooldownLeft, nextFire, beamOffAt; public Vector3 vel;
    }

    static readonly Color ReadyColor = new Color(0.42f, 0.92f, 0.68f, 1f);
    static readonly Color CoolColor = new Color(0.55f, 0.14f, 0.14f, 1f);

    readonly List<Drone> _drones = new List<Drone>();
    readonly List<UnitHealthController> _enemies = new List<UnitHealthController>();
    float _rescanAt;
    Material _bodyMat, _beamMat;
    int _blockMask = -2;
    int _lastMag = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindFirstObjectByType<SkyPrisonFloatingDrones>() != null) return;
        var go = new GameObject("[FloatingDrones]");
        DontDestroyOnLoad(go);
        go.AddComponent<SkyPrisonFloatingDrones>();
    }

    void Update()
    {
        var player = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        int want = player != null ? SkyPrisonAfpDrones.Count : 0;
        if (want != _drones.Count) Resize(want, player);
        if (want == 0 || player == null || Time.timeScale <= 0f) return;

        int mag = SkyPrisonAfpDrones.Magazine;
        if (mag != _lastMag) { _lastMag = mag; foreach (var d in _drones) if (d.cooldownLeft <= 0f) d.shots = mag; }      // 弹仓升级：没在冷却的炮直接补满新容量

        if (Time.time >= _rescanAt) { _rescanAt = Time.time + 1f; Rescan(); }

        // 站位(按用户画的草图)：两个在头侧偏上，两个在腰侧偏外偏低；炮数不足 4 个时按 左上、右上、下中 的顺序先占
        float sc = Mathf.Clamp(CharacterHeight(player) / 1.8f, 0.6f, 2f);
        Vector3 basePos = player.transform.position;
        float cd = SkyPrisonAfpDrones.Cooldown;
        for (int i = 0; i < _drones.Count; i++)
        {
            var d = _drones[i];
            // 位置：肩上方一道弧形，轻微上下浮动，带一点跟随延迟
            Vector2 slot = Slot(i, _drones.Count);
            Vector3 want3 = basePos + new Vector3(slot.x * sc, slot.y * sc + Mathf.Sin(Time.time * 2f + i * 1.7f) * 0.07f, -0.3f);
            d.tr.position = Vector3.SmoothDamp(d.tr.position, want3, ref d.vel, 0.12f);

            if (d.beam.enabled && Time.time >= d.beamOffAt) d.beam.enabled = false;

            if (d.cooldownLeft > 0f)
            {
                d.cooldownLeft -= Time.deltaTime;
                if (d.cooldownLeft <= 0f) { d.cooldownLeft = 0f; d.shots = mag; }
            }
            d.mat.color = d.cooldownLeft > 0f ? CoolColor : ReadyColor;

            if (d.cooldownLeft > 0f || d.shots <= 0 || Time.time < d.nextFire) continue;
            if (TryFire(d, player, i))
            {
                d.shots--;
                d.nextFire = Time.time + SkyPrisonAfpDrones.FireInterval;
                if (d.shots <= 0) d.cooldownLeft = cd;
            }
        }
    }

    // 站位表：[0]左上 [1]右上 [2]左下 [3]右下；3 个炮时第三个放下中
    static readonly Vector2[] Slots = { new Vector2(-0.65f, 1.4f), new Vector2(0.65f, 1.4f), new Vector2(-0.95f, 0.55f), new Vector2(0.95f, 0.55f) };
    static Vector2 Slot(int i, int count)
    {
        if (count == 3 && i == 2) return new Vector2(0f, 0.45f);
        return Slots[Mathf.Clamp(i, 0, 3)];
    }

    float _heightCache = -1f; Transform _heightFor;
    float CharacterHeight(SkyPrisonUnitRuntimeIdentity player)
    {
        if (_heightFor != player.transform) { _heightFor = player.transform; _heightCache = -1f; }
        if (_heightCache > 0f) return _heightCache;
        var col = player.GetComponentInChildren<Collider>();
        _heightCache = col != null && col.bounds.size.y > 0.3f ? col.bounds.size.y : 1.8f;
        return _heightCache;
    }

    bool TryFire(Drone d, SkyPrisonUnitRuntimeIdentity player, int index)
    {
        UnitHealthController best = null; float bestDist = float.MaxValue; Vector3 bestPoint = default;
        Vector3 origin = d.tr.position;
        for (int i = 0; i < _enemies.Count; i++)
        {
            var h = _enemies[i];
            if (h == null || h.IsDead || !h.gameObject.activeInHierarchy) continue;
            Vector3 pp = player.transform.position, hp = h.transform.position;
            float flat = Vector2.Distance(new Vector2(pp.x, pp.z), new Vector2(hp.x, hp.z));
            if (flat > SkyPrisonAfpDrones.Range || flat >= bestDist) continue;
            if (!UnitCombatHitbox.AreHostile(player.gameObject, h.gameObject)) continue;
            var col = h.GetComponentInChildren<Collider>();
            Vector3 point = col != null ? col.bounds.center : hp + Vector3.up;
            if (!HasLineOfSight(origin, point, h.transform)) continue;
            best = h; bestDist = flat; bestPoint = point;
        }
        if (best == null) return false;

        // 激光：瞬时命中
        d.beam.SetPosition(0, origin); d.beam.SetPosition(1, bestPoint);
        d.beam.enabled = true; d.beamOffAt = Time.time + 0.09f;

        var stats = player.GetComponentInChildren<UnitBattleStatRuntime>(true);
        float dmg = (stats != null ? stats.GetFinalValue("atk", 10f) : 10f) * SkyPrisonAfpDrones.DamageRatio * SkyPrisonAfpUpgrades.AttackMultiplier(stats, true);
        if (dmg > 0f)
        {
            var bridge = best.GetComponent<UnitOverheadHealthBridge>() ?? best.GetComponentInChildren<UnitOverheadHealthBridge>(true);
            bridge?.ShowDamageNumberByKey("shock", dmg, false, false, false);
            best.ApplyDamage(dmg);
        }
        return true;
    }

    // 视线：origin 到 point 之间有墙就算被挡(跟弹道一样只认 World3D / StaticObstacle，不含触发器和地形)
    bool HasLineOfSight(Vector3 origin, Vector3 point, Transform target)
    {
        if (_blockMask == -2)
        {
            _blockMask = 0;
            foreach (var n in new[] { "World3D", "StaticObstacle" }) { int l = LayerMask.NameToLayer(n); if (l >= 0) _blockMask |= 1 << l; }
        }
        if (_blockMask == 0) return true;
        Vector3 dir = point - origin; float dist = dir.magnitude;
        if (dist < 0.01f) return true;
        var hits = Physics.RaycastAll(origin, dir / dist, dist, _blockMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i].collider;
            if (c is TerrainCollider || c.transform.IsChildOf(target)) continue;
            return false;
        }
        return true;
    }

    void Rescan()
    {
        _enemies.Clear();
        var all = FindObjectsByType<UnitDefinitionRuntimeBinder>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var b = all[i];
            if (b == null || b.UnitDefinitionAsset == null || !b.gameObject.activeInHierarchy) continue;
            var ud = b.UnitDefinitionAsset;
            if (ud.defineType != UnitDefineType.Character) continue;
            if (ud.characterIdentity != CharacterIdentity.Enemy && ud.characterIdentity != CharacterIdentity.Elite && ud.characterIdentity != CharacterIdentity.Boss) continue;
            var h = b.GetComponentInChildren<UnitHealthController>(true) ?? b.GetComponentInParent<UnitHealthController>();
            if (h != null) _enemies.Add(h);
        }
    }

    void Resize(int want, SkyPrisonUnitRuntimeIdentity player)
    {
        while (_drones.Count > want)
        {
            var d = _drones[_drones.Count - 1]; _drones.RemoveAt(_drones.Count - 1);
            if (d.tr != null) Destroy(d.tr.gameObject);
        }
        while (_drones.Count < want && player != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            if (_bodyMat == null) _bodyMat = new Material(shader);
            if (_beamMat == null) { _beamMat = new Material(shader); _beamMat.color = new Color(0.55f, 1f, 0.8f, 1f); }
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Drone" + _drones.Count;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 0.32f;
            go.transform.position = player.transform.position + Vector3.up * 2.3f;
            go.layer = player.gameObject.layer;
            var rend = go.GetComponent<Renderer>(); var mat = new Material(_bodyMat); rend.sharedMaterial = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rend.receiveShadows = false;
            var lrGo = new GameObject("Beam"); lrGo.transform.SetParent(go.transform, false); lrGo.layer = go.layer;
            var lr = lrGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true; lr.positionCount = 2; lr.widthMultiplier = 0.06f; lr.sharedMaterial = _beamMat; lr.enabled = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _drones.Add(new Drone { tr = go.transform, rend = rend, mat = mat, beam = lr, shots = SkyPrisonAfpDrones.Magazine, nextFire = Time.time + _drones.Count * 0.11f });
        }
        _lastMag = SkyPrisonAfpDrones.Magazine;
    }
}
