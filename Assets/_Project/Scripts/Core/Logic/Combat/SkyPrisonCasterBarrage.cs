using System.Collections;
using UnityEngine;

/// <summary>
/// 咏唱者（キャスター）的三路弹幕：不动，玩家进入射程后先在地面画出三条预警线，
/// 依次（错开时间）沿 X 发射三发弹。三路是深度（Z）方向上并排的三条平行弹道，
/// 玩家靠换深度躲，所以线与线之间必须留得下一个人。弹的伤害、阵营判断、撞墙都
/// 复用 SkyPrisonSwordQiProjectile，只是 skill 换成咏唱者自己的属性弹技能。
/// 占位阶段：弹体与预警线都用代码生成的简单几何，Spine/特效做好之后再换。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonCasterBarrage : MonoBehaviour
{
    [Header("技能（属性弹）")]
    [SerializeField] private SkillDefinition boltSkill;
    [Tooltip("预警线与弹体的颜色。跟随技能的属性色，留空(0,0,0,0)则按 boltSkill 第一条属性自动取。")]
    [SerializeField] private Color overrideColor = new Color(0f, 0f, 0f, 0f);

    [Header("三路")]
    [Tooltip("上下两路相对正前方偏开的角度(度，俯视面内)。三路从枪口扇形散开，远处缝越来越宽。")]
    [SerializeField] private float sideAngleDegrees = 35f;
    [Tooltip("上下两路出生点相对中路的深度错开(米)，避免近处三发叠成一团。")]
    [SerializeField] private float sideOriginOffset = 0.5f;
    [SerializeField] private float muzzleHeight = 1.2f;

    [Header("节奏")]
    [SerializeField] private float triggerRangeX = 16f;
    [Tooltip("玩家与咏唱者的深度差超过这个值就不触发（够不着三路）。")]
    [SerializeField] private float triggerRangeZ = 6f;
    [Tooltip("发射前的蓄势时间(秒)。")]
    [SerializeField] private float telegraphSeconds = 0.8f;
    [Tooltip("是否画地面预警线。默认关：靠蓄势时间和三路错开的节奏让玩家读招。")]
    [SerializeField] private bool showTelegraphLines = false;
    [Tooltip("放完最后一发后的硬直(秒)：这段时间原地不动，也不会开始下一轮。")]
    [SerializeField] private float recoverySeconds = 1.5f;
    [Tooltip("三路之间错开的发射间隔。")]
    [SerializeField] private float laneStaggerSeconds = 0.3f;
    [SerializeField] private float cooldownSeconds = 3.0f;
    [Tooltip("发射后预警线再保留多久。")]
    [SerializeField] private float lineLingerSeconds = 0.15f;

    [Header("预警线")]
    [SerializeField] private float lineWidth = 0.1f;
    [SerializeField] private float lineGroundOffset = 0.05f;
    [SerializeField] private float lineMaxLength = 18f;

    private UnitActionModuleRuntime _actionModule;
    private UnitDeathController _death;
    private SkyPrisonRangedPositioning _positioning;
    private float _nextReadyTime;
    private bool _busy;
    private Color _color = new Color(1f, 0.45f, 0.25f, 1f);
    private static Material _lineMaterial;

    private void Awake()
    {
        _positioning = GetComponent<SkyPrisonRangedPositioning>();
        _color = ResolveColor();
        EnsureRefs();
    }

    // 摆在场景里的单位：行动模组等子组件是运行时才创建的，Awake 时多半还没有——引用在用到时补取。
    private void EnsureRefs()
    {
        if (_actionModule == null) _actionModule = GetComponentInChildren<UnitActionModuleRuntime>(true);
        if (_death == null) _death = GetComponentInChildren<UnitDeathController>(true);
    }

    private void Update()
    {
        EnsureRefs();
        if (_busy || Time.time < _nextReadyTime) return;
        if (_death != null && _death.IsDeadLike) return;
        if (boltSkill == null || boltSkill.projectile == null) return;

        var playerUnit = SkyPrisonPlayerAuthority.CurrentPlayerUnit;
        if (playerUnit == null) return;

        Vector3 delta = playerUnit.transform.position - transform.position;
        if (Mathf.Abs(delta.x) > triggerRangeX || Mathf.Abs(delta.z) > triggerRangeZ) return;
        if (Mathf.Abs(delta.x) < 0.05f) return;
        // 有站位组件时，要走到能打到的位置才开火
        if (_positioning != null && !_positioning.IsInFiringPosition(delta)) return;

        StartCoroutine(BarrageRoutine(delta.x >= 0f ? 1f : -1f));
    }

    private IEnumerator BarrageRoutine(float dirX)
    {
        _busy = true;
        if (_positioning != null) _positioning.Locked = true;

        // 三路的发射顺序随机：中→上→下、下→中→上……玩家不能只记一个方向换位
        int[][] orders = { new[] { 0, 1, -1 }, new[] { 0, -1, 1 }, new[] { 1, 0, -1 }, new[] { -1, 0, 1 }, new[] { 1, -1, 0 }, new[] { -1, 1, 0 } };
        int[] order = orders[Random.Range(0, orders.Length)];

        var lines = new LineRenderer[3];
        if (showTelegraphLines)
            for (int i = 0; i < 3; i++)
                lines[i] = CreateLine(order[i], dirX);

        // 预警：线从半透明渐变到亮，最后一小段闪一下，让玩家读到"要发了"
        float t = 0f;
        while (t < telegraphSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / telegraphSeconds);
            float flash = k > 0.8f ? (Mathf.Sin(t * 60f) * 0.5f + 0.5f) * 0.4f : 0f;
            SetLineAlpha(lines, 0.15f + 0.45f * k + flash);
            // 蓄势中玩家贴到枪口：中止这一轮，让站位组件先带它后退
            if (_positioning != null && _positioning.PlayerTooClose)
            {
                DestroyLines(lines);
                _positioning.Locked = false;
                _nextReadyTime = Time.time + 0.4f;
                _busy = false;
                yield break;
            }
            if (_death != null && _death.IsDeadLike) { DestroyLines(lines); _busy = false; if (_positioning != null) _positioning.Locked = false; yield break; }
            yield return null;
        }

        for (int i = 0; i < 3; i++)
        {
            FireLane(order[i], dirX);
            if (lines[i] != null) Destroy(lines[i].gameObject, lineLingerSeconds + i * 0.02f);
            if (i < 2 && laneStaggerSeconds > 0f) yield return new WaitForSeconds(laneStaggerSeconds);
        }

        // 硬直：放完最后一发后原地僵住，之后才开始冷却
        yield return new WaitForSeconds(recoverySeconds);

        _nextReadyTime = Time.time + cooldownSeconds;
        _busy = false;
        if (_positioning != null) _positioning.Locked = false;
    }

    private void FireLane(int laneIndex, float dirX)
    {
        if (_actionModule == null || boltSkill == null) return;

        Vector3 pos = transform.position + new Vector3(0f, muzzleHeight, laneIndex * sideOriginOffset);
        Vector3 dir = LaneDirection(laneIndex, dirX);
        var bolt = SkyPrisonSwordQiProjectile.Spawn(gameObject, _actionModule, boltSkill, pos, dir);
        if (bolt != null) AttachBoltVisual(bolt.transform);
    }

    private Vector3 LaneDirection(int laneIndex, float dirX)
    {
        float a = laneIndex * sideAngleDegrees * Mathf.Deg2Rad;
        return new Vector3(dirX * Mathf.Cos(a), 0f, Mathf.Sin(a));
    }

    // ── 占位视觉 ────────────────────────────────────────────────

    private void AttachBoltVisual(Transform parent)
    {
        var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = "CasterBoltVisual";
        var col = visual.GetComponent<Collider>();
        if (col != null) Destroy(col);
        visual.transform.SetParent(parent, false);
        visual.transform.localScale = Vector3.one * 0.5f;

        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) visual.layer = layer;

        var r = visual.GetComponent<Renderer>();
        r.material = new Material(GetLineMaterial()) { color = _color };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;

        var trail = visual.AddComponent<TrailRenderer>();
        trail.time = 0.25f;
        trail.startWidth = 0.4f;
        trail.endWidth = 0f;
        trail.material = new Material(GetLineMaterial()) { color = _color };
        trail.startColor = _color;
        trail.endColor = new Color(_color.r, _color.g, _color.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private LineRenderer CreateLine(int laneIndex, float dirX)
    {
        var go = new GameObject("CasterTelegraphLane" + laneIndex);
        int layer = LayerMask.NameToLayer("World3D");
        if (layer >= 0) go.layer = layer;
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        Vector3 start = transform.position + new Vector3(0f, lineGroundOffset, laneIndex * sideOriginOffset);
        lr.SetPosition(0, start);
        lr.SetPosition(1, start + LaneDirection(laneIndex, dirX) * lineMaxLength);
        lr.startWidth = lr.endWidth = lineWidth;
        lr.alignment = LineAlignment.View;
        lr.material = new Material(GetLineMaterial());
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        SetLineAlpha(new[] { lr }, 0.15f);
        return lr;
    }

    private void SetLineAlpha(LineRenderer[] lines, float alpha)
    {
        Color c = new Color(_color.r, _color.g, _color.b, Mathf.Clamp01(alpha));
        foreach (var lr in lines)
        {
            if (lr == null) continue;
            lr.startColor = lr.endColor = c;
            if (lr.material != null) lr.material.color = c;
        }
    }

    private static void DestroyLines(LineRenderer[] lines)
    {
        foreach (var lr in lines)
            if (lr != null) Destroy(lr.gameObject);
    }

    private static Material GetLineMaterial()
    {
        if (_lineMaterial == null)
            _lineMaterial = new Material(Shader.Find("Sprites/Default"));
        return _lineMaterial;
    }

    private Color ResolveColor()
    {
        if (overrideColor.a > 0.001f) return overrideColor;
        string key = boltSkill != null && boltSkill.attributeHits != null && boltSkill.attributeHits.Count > 0
            ? boltSkill.attributeHits[0].attributeKey : "heat";
        switch (key)
        {
            case "shock": return new Color(0.45f, 0.75f, 1f, 1f);
            case "corrosion": return new Color(0.45f, 1f, 0.55f, 1f);
            case "freeze": return new Color(0.65f, 0.9f, 1f, 1f);
            default: return new Color(1f, 0.45f, 0.25f, 1f);
        }
    }
}
