using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 地面探针：在 Scene 视图里点一个位置，打印角色脚底实际会解析到什么。
///
/// TerrainGroundMotor 找地面时会同时算两条来源，再按「离脚底垂直距离最近」取胜：
///   TryFindTerrainGround   → Terrain.SampleHeight
///   TryFindPhysicsGround   → 向下 Raycast 打碰撞体
/// 「为什么被绊住」这类问题靠读代码判断不了，得知道那个点上这两条各自返回什么。
///
/// 另外会打印一条横切剖面（沿点击点左右各若干米采样地形高度），
/// 地形上有没有埂/沟一眼能看出来——这是纯视觉物件不可能造成、只能来自高度图的东西。
///
/// 查完删。
/// </summary>
public class SkyPrisonGroundProbeWindow : EditorWindow
{
    private bool picking;
    private float profileLength = 6f;
    private int profileSamples = 25;
    private Vector3 profileDir = Vector3.right;
    private string report = "（还没采样）";
    private Vector2 scroll;

    /// 上次采样命中的碰撞体，用来在窗口里直接点选——光给路径字符串还得手动去层级里翻，
    /// 而这些节点名字长得几乎一样（只差结尾编号），肉眼对很容易认错。
    private Collider[] lastHitColliders = new Collider[0];

    [MenuItem("Tools/Sky Prison/Diagnostics/地面探针")]
    public static void Open()
    {
        var w = GetWindow<SkyPrisonGroundProbeWindow>("地面探针");
        w.minSize = new Vector2(520f, 420f);
        w.Show();
    }

    private void OnEnable() => SceneView.duringSceneGui += OnSceneGUI;
    private void OnDisable() => SceneView.duringSceneGui -= OnSceneGUI;

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "点下面的按钮进入取点状态，然后在 Scene 视图里点一下要检查的位置（比如铁轨上、和旁边的平地各点一次做对比）。",
            MessageType.Info);

        picking = GUILayout.Toggle(picking, picking ? "■ 取点中……（在 Scene 视图里点击）" : "▶ 开始取点",
            "Button", GUILayout.Height(30f));

        EditorGUILayout.Space(4f);
        profileLength = EditorGUILayout.Slider("剖面长度(米)", profileLength, 1f, 30f);
        profileSamples = EditorGUILayout.IntSlider("剖面采样点数", profileSamples, 5, 60);
        profileDir = EditorGUILayout.Vector3Field("剖面方向", profileDir);

        if (lastHitColliders.Length > 0)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("上次命中的碰撞体（点击直接选中）", EditorStyles.boldLabel);
            for (int i = 0; i < lastHitColliders.Length; i++)
            {
                Collider c = lastHitColliders[i];
                if (c == null) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[{i}] {c.name}", GUILayout.MinWidth(180f));
                EditorGUILayout.LabelField(c.GetType().Name, GUILayout.Width(110f));
                if (GUILayout.Button("选中", GUILayout.Width(50f)))
                {
                    Selection.activeGameObject = c.gameObject;
                    EditorGUIUtility.PingObject(c.gameObject);
                }
                if (GUILayout.Button("禁用", GUILayout.Width(50f)))
                {
                    Undo.RecordObject(c, "Disable Collider");
                    c.enabled = false;
                    EditorUtility.SetDirty(c);
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(6f);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.SelectableLabel(report, EditorStyles.wordWrappedLabel,
            GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private void OnSceneGUI(SceneView view)
    {
        if (!picking) return;

        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0) return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        Vector3 point;

        if (Physics.Raycast(ray, out RaycastHit hit, 5000f, ~0, QueryTriggerInteraction.Ignore))
            point = hit.point;
        else
        {
            // 没打到碰撞体就求与 y=0 平面的交点，至少能拿到一个 XZ
            Plane p = new Plane(Vector3.up, Vector3.zero);
            if (!p.Raycast(ray, out float d)) return;
            point = ray.GetPoint(d);
        }

        Probe(point);
        picking = false;
        e.Use();
        Repaint();
    }

    private void Probe(Vector3 point)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"采样点  X={point.x:0.###}  Y={point.y:0.###}  Z={point.z:0.###}");
        sb.AppendLine();

        // ── 地形来源 ──
        sb.AppendLine("【地形 Terrain.SampleHeight】");
        Terrain[] terrains = Terrain.activeTerrains;
        if (terrains.Length == 0)
            sb.AppendLine("  场景里没有 active Terrain");
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain t = terrains[i];
            Vector3 tp = t.transform.position;
            Vector3 size = t.terrainData.size;
            bool inside = point.x >= tp.x && point.x <= tp.x + size.x &&
                          point.z >= tp.z && point.z <= tp.z + size.z;
            if (!inside)
            {
                sb.AppendLine($"  {t.name}：采样点在地形范围外");
                continue;
            }
            float y = t.SampleHeight(point) + tp.y;
            sb.AppendLine($"  {t.name}：高度 = {y:0.####}");
        }
        sb.AppendLine();

        // ── 物理来源 ──
        sb.AppendLine("【向下 Raycast 命中的碰撞体】（从 采样点+4m 往下打 16m，全层）");
        Vector3 origin = point + Vector3.up * 4f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 16f, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
            sb.AppendLine("  没有命中任何碰撞体");
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        lastHitColliders = new Collider[hits.Length];
        for (int i = 0; i < hits.Length; i++)
            lastHitColliders[i] = hits[i].collider;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            sb.AppendLine($"  [{i}] {GetPath(h.collider.transform)}");
            sb.AppendLine($"       类型={h.collider.GetType().Name}  层={LayerMask.LayerToName(h.collider.gameObject.layer)}" +
                          $"  触发器={h.collider.isTrigger}");
            sb.AppendLine($"       命中Y={h.point.y:0.####}  法线与上方向夹角={Vector3.Angle(h.normal, Vector3.up):0.#}°");
        }
        sb.AppendLine();

        // ── 地形横切剖面 ──
        sb.AppendLine($"【地形高度剖面】沿 {profileDir.normalized} 方向，全长 {profileLength}m，{profileSamples} 点");
        sb.AppendLine("  纯视觉物件不会影响这条剖面。这里出现埂或沟，就是高度图被改过。");
        Vector3 dir = profileDir.sqrMagnitude < 0.0001f ? Vector3.right : profileDir.normalized;
        Terrain main = terrains.Length > 0 ? terrains[0] : null;
        if (main == null)
            sb.AppendLine("  没有 Terrain，跳过");
        else
        {
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            var line = new StringBuilder();
            for (int i = 0; i < profileSamples; i++)
            {
                float t01 = profileSamples <= 1 ? 0.5f : i / (float)(profileSamples - 1);
                float offset = (t01 - 0.5f) * profileLength;
                Vector3 p = point + dir * offset;
                float y = main.SampleHeight(p) + main.transform.position.y;
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
                line.Append($"  {offset,6:+0.00;-0.00}m → {y:0.####}\n");
            }
            sb.Append(line);
            sb.AppendLine($"  最低 {min:0.####}   最高 {max:0.####}   高差 = {(max - min):0.####} m");
            sb.AppendLine(max - min > 0.05f
                ? "  ⚠ 高差超过 5cm，地形在这一段确实有起伏——绊住的来源是高度图，不是装饰物。"
                : "  高差很小，这一段地形是平的，绊住的原因不在地形高度。");
        }

        report = sb.ToString();
        Debug.Log("[地面探针]\n" + report);
    }

    private static string GetPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        while (t.parent != null)
        {
            t = t.parent;
            sb.Insert(0, t.name + "/");
        }
        return sb.ToString();
    }
}
