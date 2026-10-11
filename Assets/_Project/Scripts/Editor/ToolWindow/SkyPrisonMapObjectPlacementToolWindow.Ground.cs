using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 「接地」：把 Scene 里选中的物体（单位、装饰物、白模等）的原点重新落到脚下最近的可站立表面上。
/// 用于物体陷进高台、悬空等情况。单位的原点就是脚，所以直接把原点放到地面命中点。
/// </summary>
public partial class SkyPrisonMapObjectPlacementToolWindow
{
    private const float GroundSnapRayStartHeight = 8f;   // 射线起点比当前原点高多少（够穿出陷进去的高台）
    private const float GroundSnapRayLength = 60f;
    private const float GroundSnapMaxLift = 3f;          // 陷进实体时最多往上抬多少
    private const float GroundSnapMinNormalY = 0.5f;    // 法线朝上的面才算地面，墙面不算

    private void DrawGroundSnapToolbarButton()
    {
        bool hasSelection = Selection.transforms != null && Selection.transforms.Length > 0;
        using (new EditorGUI.DisabledScope(!hasSelection))
        {
            if (GUILayout.Button(new GUIContent("接地",
                "把选中的物体落到脚下最近的可站立表面上（陷进高台里的会被抬上来，悬空的会落下去）。支持 Ctrl+Z。"),
                EditorStyles.toolbarButton, GUILayout.Width(44f)))
                SnapSelectionToGround();
        }
    }

    private static void SnapSelectionToGround()
    {
        Physics.SyncTransforms();
        Transform[] roots = Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.Editable);
        int moved = 0, missed = 0;
        string report = null;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("接地");
        int group = Undo.GetCurrentGroup();

        foreach (Transform t in roots)
        {
            if (t == null) continue;
            if (!TryFindGroundBelow(t, out Vector3 point, out string surface))
            {
                missed++;
                continue;
            }

            Vector3 pos = t.position;
            if (Mathf.Abs(pos.y - point.y) < 0.001f) continue;

            Undo.RecordObject(t, "接地");
            float before = pos.y;
            pos.y = point.y;
            t.position = pos;
            moved++;
            report = $"{t.name}: Y {before:0.00} → {pos.y:0.00}（{surface}）";
        }

        Undo.CollapseUndoOperations(group);
        Physics.SyncTransforms();

        if (moved == 1 && missed == 0)
            Debug.Log("[接地] " + report);
        else
            Debug.Log($"[接地] 移动 {moved} 个，脚下没找到地面 {missed} 个。");
    }

    private static bool TryFindGroundBelow(Transform root, out Vector3 point, out string surface)
    {
        point = default;
        surface = null;

        var own = new HashSet<Collider>(root.GetComponentsInChildren<Collider>(true));
        Vector3 origin = root.position + Vector3.up * GroundSnapRayStartHeight;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, GroundSnapRayLength, ~0, QueryTriggerInteraction.Ignore);

        // 脚是不是已经埋进某个实体里：在脚上方一点放一个小球，碰到别的碰撞体就是陷进去了。
        // 不能只看「最近的朝上表面」——高台底下同一高度还有地形面，陷进高台的脚离地形面是 0，会误判成已经站稳。
        bool sunk = false;
        Collider[] overlaps = Physics.OverlapSphere(root.position + Vector3.up * 0.1f, 0.05f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (!own.Contains(overlaps[i])) { sunk = true; break; }
        }

        float feetY = root.position.y;
        float bestY = float.MinValue;
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit h = hits[i];
            if (own.Contains(h.collider)) continue;
            if (h.normal.y < GroundSnapMinNormalY) continue;

            if (sunk)
            {
                // 陷进去：取脚上方不远处最高的那层顶面（高台顶），头顶更高的天花板不算。
                if (h.point.y < feetY - 0.001f || h.point.y > feetY + GroundSnapMaxLift) continue;
                if (h.point.y > bestY) { bestY = h.point.y; point = h.point; surface = h.collider.name; found = true; }
            }
            else
            {
                // 没陷进去：落到脚下（含脚下一点点）最高的一层；上面的台面、天花板都不算。
                if (h.point.y > feetY + 0.05f) continue;
                float d = feetY - h.point.y;
                if (d < bestDist) { bestDist = d; point = h.point; surface = h.collider.name; found = true; }
            }
        }
        return found;
    }
}
