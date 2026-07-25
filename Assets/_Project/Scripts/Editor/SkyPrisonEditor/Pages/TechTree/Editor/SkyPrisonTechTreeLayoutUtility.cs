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
        TechTreeGraphAsset.LayoutMode mode)
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

        return result;
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
