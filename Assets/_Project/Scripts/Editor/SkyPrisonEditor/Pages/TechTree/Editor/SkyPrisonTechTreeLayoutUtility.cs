using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class SkyPrisonTechTreeLayoutUtility
{
    public const float CanvasPadding = 40f;
    public const float NodeWidth = 154f;
    public const float NodeHeight = 106f;
    public const float NodeHorizontalSpacing = 64f;
    public const float NodeVerticalSpacing = 92f;

    public static Dictionary<int, Rect> BuildNodeLayout(
        int nodeCount,
        System.Func<int, int> getParentIndex,
        TechTreeGraphAsset.LayoutMode mode,
        System.Func<int, IList<int>> getExtraParents = null)
    {
        Dictionary<int, Rect> result = new Dictionary<int, Rect>();
        if (nodeCount <= 0)
            return result;

        List<int> roots = new List<int>();
        Dictionary<int, List<int>> childrenMap = new Dictionary<int, List<int>>();

        for (int i = 0; i < nodeCount; i++)
            childrenMap[i] = new List<int>();

        for (int i = 0; i < nodeCount; i++)
        {
            int parentIndex = getParentIndex(i);
            if (parentIndex < 0 || parentIndex >= nodeCount || parentIndex == i)
                roots.Add(i);
            else
                childrenMap[parentIndex].Add(i);
        }

        if (roots.Count == 0)
            roots.Add(0);

        switch (mode)
        {
            case TechTreeGraphAsset.LayoutMode.Horizontal:
                BuildHorizontalLayout(roots, childrenMap, result);
                break;
            case TechTreeGraphAsset.LayoutMode.RadialOutward:
                BuildRadialLayoutStable(roots, childrenMap, result, outward: true);
                break;
            case TechTreeGraphAsset.LayoutMode.RadialInward:
                BuildRadialLayoutStable(roots, childrenMap, result, outward: false);
                break;
            default:
                BuildVerticalLayout(roots, childrenMap, result);
                break;
        }

        // 有「多个前置才能解锁」的节点(次要前置)时，纵向/横向改用分层布局：节点按层(层 = 最深前置的层 + 1)排，
        // 横向位置取所有前置的平均位置、同层互相让开，所以这种节点会落在它的几个前置的中间下方，两条线汇合到它。
        // 没有多前置节点的图，仍用原来的子树布局，外观不变。圆形布局只画连线，不调位置。
        if (getExtraParents != null
            && (mode == TechTreeGraphAsset.LayoutMode.Vertical || mode == TechTreeGraphAsset.LayoutMode.Horizontal))
        {
            Dictionary<int, Rect> layered = BuildLayeredLayout(nodeCount, getParentIndex, getExtraParents, mode == TechTreeGraphAsset.LayoutMode.Vertical, roots, childrenMap);
            if (layered != null)
                return layered;
        }

        return result;
    }

    // ── 分层 + 重心排序布局(只在存在多前置节点时使用) ──────────────────────────────
    private static Dictionary<int, Rect> BuildLayeredLayout(
        int n,
        System.Func<int, int> getParent,
        System.Func<int, IList<int>> getExtra,
        bool vertical,
        List<int> roots,
        Dictionary<int, List<int>> primaryChildren)
    {
        List<int>[] parents = new List<int>[n];
        bool anyMulti = false;
        for (int i = 0; i < n; i++)
        {
            parents[i] = new List<int>();
            int p = getParent(i);
            if (p >= 0 && p < n && p != i)
                parents[i].Add(p);
            IList<int> extra = getExtra(i);
            if (extra != null)
                foreach (int e in extra)
                    if (e >= 0 && e < n && e != i && !parents[i].Contains(e))
                        parents[i].Add(e);
            if (parents[i].Count >= 2)
                anyMulti = true;
        }
        if (!anyMulti)
            return null;

        // 层 = 最深前置的层 + 1(没有前置的是 0)；有环的节点放到第 0 层兜底
        int[] depth = new int[n];
        for (int i = 0; i < n; i++) depth[i] = parents[i].Count == 0 ? 0 : -1;
        for (int pass = 0; pass <= n; pass++)
        {
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                if (parents[i].Count == 0) continue;
                int m = -1; bool ok = true;
                foreach (int p in parents[i])
                {
                    if (depth[p] < 0) { ok = false; break; }
                    m = Mathf.Max(m, depth[p]);
                }
                if (ok && depth[i] != m + 1) { depth[i] = m + 1; changed = true; }
            }
            if (!changed) break;
        }
        for (int i = 0; i < n; i++) if (depth[i] < 0) depth[i] = 0;

        // 同层的初始顺序：沿主前置树的深度优先顺序
        int[] rank = new int[n];
        int counter = 0;
        bool[] visited = new bool[n];
        foreach (int r in roots) AssignRank(r, primaryChildren, rank, visited, ref counter);
        for (int i = 0; i < n; i++) if (!visited[i]) AssignRank(i, primaryChildren, rank, visited, ref counter);

        int maxDepth = 0;
        for (int i = 0; i < n; i++) maxDepth = Mathf.Max(maxDepth, depth[i]);
        List<int>[] layers = new List<int>[maxDepth + 1];
        for (int d = 0; d <= maxDepth; d++) layers[d] = new List<int>();
        for (int i = 0; i < n; i++) layers[depth[i]].Add(i);
        for (int d = 0; d <= maxDepth; d++) layers[d].Sort((a, b) => rank[a].CompareTo(rank[b]));

        List<int>[] kids = new List<int>[n];
        for (int i = 0; i < n; i++) kids[i] = new List<int>();
        for (int i = 0; i < n; i++) foreach (int p in parents[i]) kids[p].Add(i);

        float crossStep = vertical ? NodeWidth + NodeHorizontalSpacing : NodeHeight + NodeVerticalSpacing;
        float mainStep = vertical ? NodeHeight + NodeVerticalSpacing : NodeWidth + NodeHorizontalSpacing;

        float[] x = new float[n];
        for (int d = 0; d <= maxDepth; d++)
            for (int k = 0; k < layers[d].Count; k++)
                x[layers[d][k]] = (k - (layers[d].Count - 1) * 0.5f) * crossStep;

        for (int round = 0; round < 6; round++)
        {
            for (int d = 1; d <= maxDepth; d++)
            {
                float[] desired = new float[n];
                foreach (int i in layers[d])
                {
                    float sum = 0f;
                    foreach (int p in parents[i]) sum += x[p];
                    desired[i] = parents[i].Count > 0 ? sum / parents[i].Count : x[i];
                }
                PlaceLayer(layers[d], desired, x, rank, crossStep);
            }
            for (int d = maxDepth - 1; d >= 0; d--)
            {
                float[] desired = new float[n];
                foreach (int i in layers[d])
                {
                    if (kids[i].Count == 0) { desired[i] = x[i]; continue; }
                    float sum = 0f;
                    foreach (int c in kids[i]) sum += x[c];
                    desired[i] = sum / kids[i].Count;
                }
                PlaceLayer(layers[d], desired, x, rank, crossStep);
            }
        }

        float half = vertical ? NodeWidth * 0.5f : NodeHeight * 0.5f;
        float minCross = float.MaxValue;
        for (int i = 0; i < n; i++) minCross = Mathf.Min(minCross, x[i] - half);
        float shift = CanvasPadding - minCross;

        Dictionary<int, Rect> result = new Dictionary<int, Rect>();
        for (int i = 0; i < n; i++)
        {
            float cross = x[i] + shift;
            float main = CanvasPadding + depth[i] * mainStep;
            result[i] = vertical
                ? new Rect(cross - NodeWidth * 0.5f, main, NodeWidth, NodeHeight)
                : new Rect(main, cross - NodeHeight * 0.5f, NodeWidth, NodeHeight);
        }
        return result;
    }

    private static void AssignRank(int node, Dictionary<int, List<int>> childrenMap, int[] rank, bool[] visited, ref int counter)
    {
        if (node < 0 || node >= visited.Length || visited[node]) return;
        visited[node] = true;
        rank[node] = counter++;
        if (!childrenMap.TryGetValue(node, out List<int> kids)) return;
        foreach (int k in kids) AssignRank(k, childrenMap, rank, visited, ref counter);
    }

    // 把一层里的节点放到「想去的位置」附近，同层互相至少隔 step：先从左往右挤、再从右往左挤，两遍取平均，左右对称不偏
    private static void PlaceLayer(List<int> layer, float[] desired, float[] x, int[] rank, float step)
    {
        layer.Sort((a, b) =>
        {
            int c = desired[a].CompareTo(desired[b]);
            return c != 0 ? c : rank[a].CompareTo(rank[b]);
        });
        int count = layer.Count;
        float[] fwd = new float[count];
        float[] bwd = new float[count];
        for (int k = 0; k < count; k++)
        {
            float want = desired[layer[k]];
            fwd[k] = k == 0 ? want : Mathf.Max(want, fwd[k - 1] + step);
        }
        for (int k = count - 1; k >= 0; k--)
        {
            float want = desired[layer[k]];
            bwd[k] = k == count - 1 ? want : Mathf.Min(want, bwd[k + 1] - step);
        }
        for (int k = 0; k < count; k++)
            x[layer[k]] = (fwd[k] + bwd[k]) * 0.5f;
    }

    private static void ApplyMultiParentAdjust(
        int nodeCount,
        System.Func<int, int> getParentIndex,
        System.Func<int, IList<int>> getExtraParents,
        bool vertical,
        Dictionary<int, List<int>> childrenMap,
        Dictionary<int, Rect> layout)
    {
        List<int> multi = new List<int>();
        for (int i = 0; i < nodeCount; i++)
        {
            IList<int> extra = getExtraParents(i);
            if (extra != null && extra.Count > 0 && layout.ContainsKey(i))
                multi.Add(i);
        }
        if (multi.Count == 0)
            return;

        // 按当前位置从上到下(从左到右)处理，前面的节点挪好了，后面依赖它的节点再挪
        multi.Sort((a, b) => (vertical ? layout[a].yMin : layout[a].xMin).CompareTo(vertical ? layout[b].yMin : layout[b].xMin));

        foreach (int n in multi)
        {
            List<int> parents = new List<int>();
            int primary = getParentIndex(n);
            if (primary >= 0 && layout.ContainsKey(primary))
                parents.Add(primary);
            foreach (int e in getExtraParents(n))
                if (e != n && layout.ContainsKey(e) && !parents.Contains(e))
                    parents.Add(e);
            if (parents.Count < 2)
                continue;

            Rect r = layout[n];

            float need = float.MinValue;
            foreach (int p in parents)
            {
                Rect pr = layout[p];
                need = Mathf.Max(need, vertical ? pr.yMax + NodeVerticalSpacing : pr.xMax + NodeHorizontalSpacing);
            }
            float dMain = Mathf.Max(0f, need - (vertical ? r.yMin : r.xMin));

            float avg = 0f;
            foreach (int p in parents)
                avg += vertical ? layout[p].center.x : layout[p].center.y;
            avg /= parents.Count;
            float dCross = avg - (vertical ? r.center.x : r.center.y);

            List<int> subtree = new List<int>();
            CollectSubtree(n, childrenMap, subtree);
            ShiftNodes(subtree, layout, vertical ? new Vector2(dCross, dMain) : new Vector2(dMain, dCross));

            // 和同一行(列)里别的节点重叠就沿横轴(纵轴)方向挪开，整棵子树一起动
            HashSet<int> moved = new HashSet<int>(subtree);
            for (int iter = 0; iter < 40; iter++)
            {
                Rect cur = layout[n];
                Rect expanded = new Rect(cur.x - 10f, cur.y - 10f, cur.width + 20f, cur.height + 20f);
                float push = 0f;
                foreach (var kv in layout)
                {
                    if (moved.Contains(kv.Key) && kv.Key != n) continue;
                    if (kv.Key == n) continue;
                    if (!expanded.Overlaps(kv.Value)) continue;
                    float need2 = vertical ? kv.Value.xMax + NodeHorizontalSpacing * 0.5f - cur.xMin : kv.Value.yMax + NodeVerticalSpacing * 0.5f - cur.yMin;
                    push = Mathf.Max(push, need2);
                }
                if (push <= 0.01f)
                    break;
                ShiftNodes(subtree, layout, vertical ? new Vector2(push, 0f) : new Vector2(0f, push));
            }
        }
    }

    private static void CollectSubtree(int root, Dictionary<int, List<int>> childrenMap, List<int> output)
    {
        output.Add(root);
        if (!childrenMap.TryGetValue(root, out List<int> kids))
            return;
        foreach (int k in kids)
            CollectSubtree(k, childrenMap, output);
    }

    private static void ShiftNodes(List<int> nodes, Dictionary<int, Rect> layout, Vector2 delta)
    {
        foreach (int i in nodes)
        {
            if (!layout.TryGetValue(i, out Rect r)) continue;
            layout[i] = new Rect(r.x + delta.x, r.y + delta.y, r.width, r.height);
        }
    }

    public static Rect CalculateBoundsRect(Dictionary<int, Rect> layout)
    {
        if (layout == null || layout.Count == 0)
            return new Rect(-100f, -100f, 200f, 200f);

        float minX = layout.Values.Min(r => r.xMin);
        float minY = layout.Values.Min(r => r.yMin);
        float maxX = layout.Values.Max(r => r.xMax);
        float maxY = layout.Values.Max(r => r.yMax);

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    private static void BuildVerticalLayout(
        List<int> roots,
        Dictionary<int, List<int>> childrenMap,
        Dictionary<int, Rect> result)
    {
        Dictionary<int, float> subtreeSpan = new Dictionary<int, float>();
        foreach (int root in roots)
            CalcSubtreeSpan(root, childrenMap, subtreeSpan);

        float cursor = CanvasPadding;
        foreach (int root in roots)
        {
            float span = subtreeSpan[root];
            float centerX = cursor + span * 0.5f;
            AssignVertical(root, 0, centerX, childrenMap, subtreeSpan, result);
            cursor += span + NodeHorizontalSpacing;
        }
    }

    private static void BuildHorizontalLayout(
        List<int> roots,
        Dictionary<int, List<int>> childrenMap,
        Dictionary<int, Rect> result)
    {
        Dictionary<int, float> subtreeSpan = new Dictionary<int, float>();
        foreach (int root in roots)
            CalcSubtreeSpan(root, childrenMap, subtreeSpan);

        float cursor = CanvasPadding;
        foreach (int root in roots)
        {
            float span = subtreeSpan[root];
            float centerY = cursor + span * 0.5f;
            AssignHorizontal(root, 0, centerY, childrenMap, subtreeSpan, result);
            cursor += span + NodeVerticalSpacing;
        }
    }

    private static void BuildRadialLayoutStable(
        List<int> roots,
        Dictionary<int, List<int>> childrenMap,
        Dictionary<int, Rect> result,
        bool outward)
    {
        Dictionary<int, int> depthMap = BuildDepthMap(roots, childrenMap);
        int maxDepth = depthMap.Count > 0 ? depthMap.Values.Max() : 0;

        Dictionary<int, int> subtreeWeight = new Dictionary<int, int>();
        foreach (int root in roots)
            CalcSubtreeWeight(root, childrenMap, subtreeWeight);

        Dictionary<int, int> depthCounts = new Dictionary<int, int>();
        foreach (int depth in depthMap.Values)
        {
            if (!depthCounts.ContainsKey(depth))
                depthCounts[depth] = 0;
            depthCounts[depth]++;
        }

        Dictionary<int, float> outwardRadiusByDepth = new Dictionary<int, float>();

        float rootCountForCircle = Mathf.Max(roots.Count, 3);
        float rootArcNeed = NodeWidth + 64f;
        float baseRadius = Mathf.Max(150f, (rootArcNeed * rootCountForCircle) / (2f * Mathf.PI));
        float prevRadius = baseRadius;

        for (int depth = 0; depth <= maxDepth; depth++)
        {
            int countAtDepth = depthCounts.ContainsKey(depth) ? depthCounts[depth] : 1;

            float minArcNeed = NodeWidth + 44f;
            float countRadius = (minArcNeed * Mathf.Max(1, countAtDepth)) / (2f * Mathf.PI);

            float layerRadius = Mathf.Max(prevRadius, countRadius);
            if (depth > 0)
                layerRadius = Mathf.Max(layerRadius, prevRadius + NodeHeight + 96f);

            outwardRadiusByDepth[depth] = layerRadius;
            prevRadius = layerRadius;
        }

        Dictionary<int, float> finalRadiusByDepth = new Dictionary<int, float>();
        if (outward)
        {
            foreach (var kv in outwardRadiusByDepth)
                finalRadiusByDepth[kv.Key] = kv.Value + kv.Key * 10f;
        }
        else
        {
            for (int depth = 0; depth <= maxDepth; depth++)
                finalRadiusByDepth[depth] = outwardRadiusByDepth[maxDepth - depth];

            if (maxDepth > 0)
            {
                for (int depth = 0; depth <= maxDepth; depth++)
                    finalRadiusByDepth[depth] += depth * 22f;
            }
        }

        float rootStep = 360f / Mathf.Max(1, roots.Count);
        float startAngle = -90f;

        for (int i = 0; i < roots.Count; i++)
        {
            int root = roots[i];
            float rootAngle = startAngle + rootStep * i;
            float rootSector = GetRootSectorSpan(roots.Count);

            LayoutRadialNodeSymmetric(
                root,
                0,
                rootAngle,
                rootSector,
                Vector2.zero,
                childrenMap,
                subtreeWeight,
                finalRadiusByDepth,
                result
            );
        }

        ResolveRadialOverlapsStable(result, Vector2.zero, depthMap, finalRadiusByDepth);
    }

    private static void LayoutRadialNodeSymmetric(
        int nodeIndex,
        int depth,
        float centerAngleDeg,
        float sectorSpanDeg,
        Vector2 center,
        Dictionary<int, List<int>> childrenMap,
        Dictionary<int, int> subtreeWeight,
        Dictionary<int, float> radiusByDepth,
        Dictionary<int, Rect> result)
    {
        float angleRad = centerAngleDeg * Mathf.Deg2Rad;
        float radius = radiusByDepth[depth];

        Vector2 nodeCenter = center + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;

        result[nodeIndex] = new Rect(
            nodeCenter.x - NodeWidth * 0.5f,
            nodeCenter.y - NodeHeight * 0.5f,
            NodeWidth,
            NodeHeight
        );

        if (!childrenMap.TryGetValue(nodeIndex, out List<int> children) || children.Count == 0)
            return;

        float childRadius = radiusByDepth.ContainsKey(depth + 1) ? radiusByDepth[depth + 1] : radius + NodeHeight + 96f;

        float minAnglePerChild = Mathf.Max(
            18f,
            Mathf.Rad2Deg * ((NodeWidth + 42f) / Mathf.Max(1f, childRadius))
        );

        float desiredSpan = Mathf.Max(
            minAnglePerChild * children.Count,
            34f + children.Count * 18f
        );

        float usableSpan = Mathf.Min(Mathf.Max(desiredSpan, sectorSpanDeg * 0.72f), sectorSpanDeg * 0.96f);
        usableSpan = Mathf.Max(usableSpan, minAnglePerChild * children.Count);

        float start = centerAngleDeg - usableSpan * 0.5f;

        float totalWeight = Mathf.Max(1f, children.Sum(c => subtreeWeight.ContainsKey(c) ? subtreeWeight[c] : 1));

        float cursor = start;
        for (int i = 0; i < children.Count; i++)
        {
            int child = children[i];
            float weight = Mathf.Max(1f, subtreeWeight.ContainsKey(child) ? subtreeWeight[child] : 1f);

            float childSpan = Mathf.Max(minAnglePerChild, usableSpan * (weight / totalWeight));
            float childCenter = cursor + childSpan * 0.5f;

            LayoutRadialNodeSymmetric(
                child,
                depth + 1,
                childCenter,
                childSpan,
                center,
                childrenMap,
                subtreeWeight,
                radiusByDepth,
                result
            );

            cursor += childSpan;
        }
    }

    private static float GetRootSectorSpan(int rootCount)
    {
        if (rootCount <= 1)
            return 300f;
        if (rootCount == 2)
            return 150f;

        float step = 360f / rootCount;
        return Mathf.Clamp(step * 0.78f, 88f, 132f);
    }

    private static void ResolveRadialOverlapsStable(
        Dictionary<int, Rect> result,
        Vector2 center,
        Dictionary<int, int> depthMap,
        Dictionary<int, float> radiusByDepth)
    {
        Dictionary<int, List<int>> byDepth = new Dictionary<int, List<int>>();
        foreach (var kv in depthMap)
        {
            if (!byDepth.ContainsKey(kv.Value))
                byDepth[kv.Value] = new List<int>();
            byDepth[kv.Value].Add(kv.Key);
        }

        const float minArcDistance = NodeWidth + 42f;

        foreach (var pair in byDepth)
        {
            List<int> layer = pair.Value;
            if (layer.Count <= 1)
                continue;

            layer.Sort((a, b) =>
            {
                float aa = GetAngleDeg(result[a].center, center);
                float bb = GetAngleDeg(result[b].center, center);
                return aa.CompareTo(bb);
            });

            float radius = radiusByDepth[pair.Key];

            for (int iter = 0; iter < 8; iter++)
            {
                bool moved = false;

                for (int i = 0; i < layer.Count; i++)
                {
                    int current = layer[i];
                    int next = layer[(i + 1) % layer.Count];

                    float a0 = GetAngleDeg(result[current].center, center);
                    float a1 = GetAngleDeg(result[next].center, center);
                    float delta = DeltaAnglePositive(a0, a1);
                    float arcLength = Mathf.Deg2Rad * delta * radius;

                    if (arcLength < minArcDistance)
                    {
                        float neededDelta = (minArcDistance / radius) * Mathf.Rad2Deg;
                        float push = (neededDelta - delta) * 0.5f;

                        Rect currentRect = result[current];
                        Rect nextRect = result[next];

                        RotateRectAround(ref currentRect, center, -push);
                        RotateRectAround(ref nextRect, center, push);

                        result[current] = currentRect;
                        result[next] = nextRect;
                        moved = true;
                    }
                }

                if (!moved)
                    break;
            }
        }
    }

    private static Dictionary<int, int> BuildDepthMap(List<int> roots, Dictionary<int, List<int>> childrenMap)
    {
        Dictionary<int, int> depthMap = new Dictionary<int, int>();
        Queue<int> q = new Queue<int>();

        foreach (int root in roots)
        {
            if (!depthMap.ContainsKey(root))
            {
                depthMap[root] = 0;
                q.Enqueue(root);
            }
        }

        while (q.Count > 0)
        {
            int current = q.Dequeue();
            int depth = depthMap[current];

            if (!childrenMap.TryGetValue(current, out List<int> children))
                continue;

            foreach (int child in children)
            {
                if (!depthMap.ContainsKey(child))
                {
                    depthMap[child] = depth + 1;
                    q.Enqueue(child);
                }
            }
        }

        return depthMap;
    }

    private static int CalcSubtreeWeight(int nodeIndex, Dictionary<int, List<int>> childrenMap, Dictionary<int, int> subtreeWeight)
    {
        if (!childrenMap.TryGetValue(nodeIndex, out List<int> children) || children.Count == 0)
        {
            subtreeWeight[nodeIndex] = 1;
            return 1;
        }

        int total = 1;
        foreach (int child in children)
            total += CalcSubtreeWeight(child, childrenMap, subtreeWeight);

        subtreeWeight[nodeIndex] = total;
        return total;
    }

    private static float CalcSubtreeSpan(int nodeIndex, Dictionary<int, List<int>> childrenMap, Dictionary<int, float> subtreeSpan)
    {
        if (!childrenMap.TryGetValue(nodeIndex, out List<int> children) || children.Count == 0)
        {
            subtreeSpan[nodeIndex] = NodeWidth;
            return NodeWidth;
        }

        float total = 0f;
        for (int i = 0; i < children.Count; i++)
        {
            total += CalcSubtreeSpan(children[i], childrenMap, subtreeSpan);
            if (i < children.Count - 1)
                total += NodeHorizontalSpacing;
        }

        float finalSpan = Mathf.Max(NodeWidth, total);
        subtreeSpan[nodeIndex] = finalSpan;
        return finalSpan;
    }

    private static void AssignVertical(
        int nodeIndex,
        int depth,
        float centerX,
        Dictionary<int, List<int>> childrenMap,
        Dictionary<int, float> subtreeSpan,
        Dictionary<int, Rect> result)
    {
        result[nodeIndex] = new Rect(
            centerX - NodeWidth * 0.5f,
            CanvasPadding + depth * (NodeHeight + NodeVerticalSpacing),
            NodeWidth,
            NodeHeight
        );

        if (!childrenMap.TryGetValue(nodeIndex, out List<int> children) || children.Count == 0)
            return;

        float total = 0f;
        for (int i = 0; i < children.Count; i++)
        {
            total += subtreeSpan[children[i]];
            if (i < children.Count - 1)
                total += NodeHorizontalSpacing;
        }

        float cursor = centerX - total * 0.5f;
        for (int i = 0; i < children.Count; i++)
        {
            float span = subtreeSpan[children[i]];
            float childCenterX = cursor + span * 0.5f;
            AssignVertical(children[i], depth + 1, childCenterX, childrenMap, subtreeSpan, result);
            cursor += span + NodeHorizontalSpacing;
        }
    }

    private static void AssignHorizontal(
        int nodeIndex,
        int depth,
        float centerY,
        Dictionary<int, List<int>> childrenMap,
        Dictionary<int, float> subtreeSpan,
        Dictionary<int, Rect> result)
    {
        result[nodeIndex] = new Rect(
            CanvasPadding + depth * (NodeWidth + NodeHorizontalSpacing),
            centerY - NodeHeight * 0.5f,
            NodeWidth,
            NodeHeight
        );

        if (!childrenMap.TryGetValue(nodeIndex, out List<int> children) || children.Count == 0)
            return;

        float total = 0f;
        for (int i = 0; i < children.Count; i++)
        {
            total += subtreeSpan[children[i]];
            if (i < children.Count - 1)
                total += NodeVerticalSpacing;
        }

        float cursor = centerY - total * 0.5f;
        for (int i = 0; i < children.Count; i++)
        {
            float span = subtreeSpan[children[i]];
            float childCenterY = cursor + span * 0.5f;
            AssignHorizontal(children[i], depth + 1, childCenterY, childrenMap, subtreeSpan, result);
            cursor += span + NodeVerticalSpacing;
        }
    }

    private static float GetAngleDeg(Vector2 point, Vector2 center)
    {
        Vector2 dir = point - center;
        return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
    }

    private static float DeltaAnglePositive(float fromDeg, float toDeg)
    {
        return Mathf.Repeat(toDeg - fromDeg, 360f);
    }

    private static void RotateRectAround(ref Rect rect, Vector2 center, float deltaDeg)
    {
        Vector2 c = rect.center;
        Vector2 dir = c - center;
        float radius = dir.magnitude;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + deltaDeg;
        Vector2 newCenter = center + new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;

        rect = new Rect(newCenter.x - rect.width * 0.5f, newCenter.y - rect.height * 0.5f, rect.width, rect.height);
    }
}
