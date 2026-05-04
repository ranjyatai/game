using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class SkyPrisonAnimationFormulaPanel
{
    private readonly SkyPrisonAnimationWorkbenchState state;

    private const float Padding = 8f;
    private const float HeaderHeight = 24f;
    private const float ControlWidth = 430f;
    private const float MinCurveWidth = 300f;
    private const float MinContentHeight = 520f;
    private const float MinStackedContentHeight = 760f;
    private const float Gap = 8f;

    private readonly Vector3[] curvePoints = new Vector3[160];

    private string activeManualAngleSliderKey = null;
    private object activeManualAngleSliderUndoSnapshot = null;
    private bool activeManualAngleSliderChanged = false;
    private bool activeManualAngleSliderUndoPushed = false;
    private object activeSampleSegmentsSliderUndoSnapshot = null;
    private bool activeSampleSegmentsSliderChanged = false;

    public SkyPrisonAnimationFormulaPanel(SkyPrisonAnimationWorkbenchState state)
    {
        this.state = state;
    }

    public void Draw(Rect rect)
    {
        EditorGUI.DrawRect(rect, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        if (rect.width < 80f || rect.height < 50f)
            return;

        GUI.BeginGroup(rect);

        Rect localRect = new Rect(0f, 0f, rect.width, rect.height);
        Rect headerRect = new Rect(0f, 0f, localRect.width, HeaderHeight);
        EditorGUI.DrawRect(headerRect, SkyPrisonAnimationWorkbenchStyle.PanelDeepBg);
        GUI.Label(new Rect(8f, 4f, 220f, 18f), "动作参数 / 节点角度", EditorStyles.boldLabel);
        GUI.Label(new Rect(localRect.width - 300f, 4f, 292f, 18f), "扫描身体节点，每个节点 -180° ~ 180°", EditorStyles.miniLabel);

        Rect scrollOuter = new Rect(0f, HeaderHeight, localRect.width, localRect.height - HeaderHeight);
        float contentWidth = Mathf.Max(scrollOuter.width - 16f, 560f);
        bool stacked = contentWidth < ControlWidth + MinCurveWidth + Gap + Padding * 2f;
        float contentHeight = stacked ? MinStackedContentHeight : Mathf.Max(MinContentHeight, scrollOuter.height - 16f);

        Rect viewRect = new Rect(0f, 0f, contentWidth, contentHeight);
        state.FormulaScroll = GUI.BeginScrollView(scrollOuter, state.FormulaScroll, viewRect, false, true);

        Rect content = new Rect(Padding, Padding, viewRect.width - Padding * 2f, viewRect.height - Padding * 2f);
        if (stacked)
            DrawStacked(content);
        else
            DrawHorizontal(content);

        GUI.EndScrollView();
        EndSliderUndoIfMouseReleased();
        GUI.EndGroup();
    }

    private void DrawHorizontal(Rect content)
    {
        Rect controls = new Rect(content.x, content.y, ControlWidth, content.height);
        Rect preview = new Rect(controls.xMax + Gap, content.y, content.width - ControlWidth - Gap, content.height);
        DrawControls(controls);
        DrawAnglePreview(preview);
    }

    private void DrawStacked(Rect content)
    {
        Rect controls = new Rect(content.x, content.y, content.width, 430f);
        Rect preview = new Rect(content.x, controls.yMax + Gap, content.width, content.height - controls.height - Gap);
        DrawControls(controls);
        DrawAnglePreview(preview);
    }

    private void DrawControls(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.15f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        float x = rect.x + 10f;
        float y = rect.y + 10f;
        float w = rect.width - 20f;

        SkyPrisonAnimationActionRow action = state.CurrentAction();
        int totalFrames = state.TimelineTotalFrames;

        GUI.Label(new Rect(x, y, w, 18f), "当前动作", EditorStyles.boldLabel);
        y += 24f;
        GUI.Label(new Rect(x, y, w, 18f), action.name + " [" + action.key + "]", EditorStyles.label);
        y += 22f;
        GUI.Label(new Rect(x, y, w, 18f), string.Format("{0:0.00}s × {1}fps = {2}帧", state.TimelineDurationSeconds, state.TimelineFrameRate, totalFrames), EditorStyles.miniLabel);
        y += 24f;

        state.ManualAngleSampleSegments = Mathf.RoundToInt(DrawSampleSegmentsSliderWithUndo(x, ref y, w, "采样段数", state.ManualAngleSampleSegments, 1, 32));
        GUI.Label(new Rect(x, y, w, 32f), "采样帧：" + state.FormatManualAngleSampleFrames(), EditorStyles.wordWrappedMiniLabel);
        y += 38f;

        state.ManualAngleReplaceExisting = GUI.Toggle(new Rect(x, y, w, 20f), state.ManualAngleReplaceExisting, "覆盖同帧同节点关键帧");
        y += 24f;

        List<SkyPrisonAnimationRigRow> rows = state.GetManualAngleTargetRows();
        GUI.Label(new Rect(x, y, w, 18f), "扫描到的身体节点角度", EditorStyles.boldLabel);
        y += 22f;
        GUI.Label(new Rect(x, y, w, 32f), "每个节点都是可写入角度参数。调好后可以写入当前帧，或铺满当前动作的采样帧。", EditorStyles.wordWrappedMiniLabel);
        y += 38f;

        Rect buttonRow = new Rect(x, rect.yMax - 68f, w, 24f);
        float buttonGap = 6f;
        float buttonW = (w - buttonGap * 2f) / 3f;
        if (GUI.Button(new Rect(buttonRow.x, buttonRow.y, buttonW, 24f), "写入当前帧"))
            state.GenerateManualAnglesToCurrentAction(false);
        if (GUI.Button(new Rect(buttonRow.x + buttonW + buttonGap, buttonRow.y, buttonW, 24f), "铺满当前动作"))
            state.GenerateManualAnglesToCurrentAction(true);
        if (GUI.Button(new Rect(buttonRow.x + (buttonW + buttonGap) * 2f, buttonRow.y, buttonW, 24f), "角度归零"))
        {
            state.ResetManualBoneAngles();
            GUI.changed = true;
        }

        GUI.Label(new Rect(x, rect.yMax - 38f, w, 28f), "写入后仍然是普通时间线关键帧，可以继续在轨道上手动修。", EditorStyles.wordWrappedMiniLabel);

        Rect listRect = new Rect(x, y, w, Mathf.Max(60f, buttonRow.y - y - 8f));
        float rowH = 30f;
        Rect listContent = new Rect(0f, 0f, Mathf.Max(10f, listRect.width - 16f), Mathf.Max(listRect.height, rows.Count * rowH + 8f));
        state.ManualAngleParameterScroll = GUI.BeginScrollView(listRect, state.ManualAngleParameterScroll, listContent, false, true);

        float ly = 4f;
        for (int i = 0; i < rows.Count; i++)
        {
            SkyPrisonAnimationRigRow row = rows[i];
            if (row == null) continue;
            DrawBoneAngleRow(0f, ref ly, listContent.width, row);
        }

        GUI.EndScrollView();
    }

    private void DrawBoneAngleRow(float x, ref float y, float w, SkyPrisonAnimationRigRow row)
    {
        float labelW = Mathf.Min(150f, w * 0.38f);
        float fieldW = 62f;
        float sliderW = Mathf.Max(50f, w - labelW - fieldW - 16f);

        Rect bg = new Rect(x, y, w, 26f);
        if (((int)(y / 30f)) % 2 == 0)
            EditorGUI.DrawRect(bg, new Color(1f, 1f, 1f, 0.025f));

        string label = string.IsNullOrEmpty(row.name) ? row.key : row.name;
        if (!string.IsNullOrEmpty(row.key))
            label += "  [" + row.key + "]";

        Rect labelRect = new Rect(x + 4f, y + 4f, labelW - 6f, 18f);
        Rect sliderRect = new Rect(x + labelW, y + 3f, sliderW, 20f);
        Rect fieldRect = new Rect(sliderRect.xMax + 6f, y + 3f, fieldW, 20f);

        GUI.Label(labelRect, label, EditorStyles.miniLabel);
        float value = state.GetManualBoneAngle(row.key);

        Event e = Event.current;
        if (e != null && e.type == EventType.MouseDown && e.button == 0 && sliderRect.Contains(e.mousePosition))
            BeginManualAngleSliderUndo(row.key);

        EditorGUI.BeginChangeCheck();
        float sliderValue = GUI.HorizontalSlider(sliderRect, value, -180f, 180f);
        if (EditorGUI.EndChangeCheck())
        {
            if (activeManualAngleSliderUndoSnapshot == null)
                BeginManualAngleSliderUndo(row.key);
            ApplyManualAngleValue(row.key, sliderValue);
            activeManualAngleSliderChanged = true;
            PushActiveManualAngleSliderUndoImmediatelyIfNeeded();
        }

        value = state.GetManualBoneAngle(row.key);
        EditorGUI.BeginChangeCheck();
        float fieldValue = EditorGUI.FloatField(fieldRect, value);
        if (EditorGUI.EndChangeCheck())
        {
            object undoSnapshot = state.CaptureStructureUndoSnapshot();
            ApplyManualAngleValue(row.key, fieldValue);
            state.PushCapturedStructureUndo(undoSnapshot);
        }

        y += 30f;
    }

    private void ApplyManualAngleValue(string rigKey, float value)
    {
        state.SetManualBoneAngle(rigKey, value);
        state.ApplyManualAngleLiveChange(rigKey);
        GUI.changed = true;
    }

    private void BeginManualAngleSliderUndo(string rigKey)
    {
        if (activeManualAngleSliderUndoSnapshot != null && activeManualAngleSliderKey == rigKey)
            return;

        EndManualAngleSliderUndo(true);
        activeManualAngleSliderKey = rigKey;
        activeManualAngleSliderUndoSnapshot = state.CaptureStructureUndoSnapshot();
        activeManualAngleSliderChanged = false;
    }

    private void PushActiveManualAngleSliderUndoImmediatelyIfNeeded()
    {
        if (activeManualAngleSliderUndoPushed)
            return;

        if (activeManualAngleSliderUndoSnapshot == null)
            return;

        state.PushCapturedStructureUndo(activeManualAngleSliderUndoSnapshot);
        activeManualAngleSliderUndoPushed = true;
    }

    private float DrawSampleSegmentsSliderWithUndo(float x, ref float y, float w, string label, float value, float leftValue, float rightValue)
    {
        float labelW = Mathf.Min(92f, w * 0.30f);
        float fieldW = Mathf.Min(72f, Mathf.Max(50f, w * 0.18f));
        float sliderW = Mathf.Max(40f, w - labelW - fieldW - 12f);

        Rect labelRect = new Rect(x, y + 2f, labelW, 18f);
        Rect sliderRect = new Rect(labelRect.xMax + 6f, y, sliderW, 20f);
        Rect fieldRect = new Rect(sliderRect.xMax + 6f, y, fieldW, 20f);

        GUI.Label(labelRect, label);

        Event e = Event.current;
        if (e != null && e.type == EventType.MouseDown && e.button == 0 && sliderRect.Contains(e.mousePosition))
        {
            activeSampleSegmentsSliderUndoSnapshot = state.CaptureStructureUndoSnapshot();
            activeSampleSegmentsSliderChanged = false;
        }

        EditorGUI.BeginChangeCheck();
        float sliderValue = GUI.HorizontalSlider(sliderRect, value, leftValue, rightValue);
        if (EditorGUI.EndChangeCheck())
        {
            if (activeSampleSegmentsSliderUndoSnapshot == null)
                activeSampleSegmentsSliderUndoSnapshot = state.CaptureStructureUndoSnapshot();
            value = Mathf.Clamp(sliderValue, leftValue, rightValue);
            activeSampleSegmentsSliderChanged = true;
            GUI.changed = true;
        }

        EditorGUI.BeginChangeCheck();
        float fieldValue = EditorGUI.FloatField(fieldRect, value);
        if (EditorGUI.EndChangeCheck())
        {
            object undoSnapshot = state.CaptureStructureUndoSnapshot();
            value = Mathf.Clamp(fieldValue, leftValue, rightValue);
            state.PushCapturedStructureUndo(undoSnapshot);
            GUI.changed = true;
        }

        y += 30f;
        return value;
    }

    private void EndSliderUndoIfMouseReleased()
    {
        Event e = Event.current;
        if (e == null) return;
        if (e.type != EventType.MouseUp && e.rawType != EventType.MouseUp && e.type != EventType.Ignore)
            return;

        EndManualAngleSliderUndo(true);
        if (activeSampleSegmentsSliderUndoSnapshot != null && activeSampleSegmentsSliderChanged)
            state.PushCapturedStructureUndo(activeSampleSegmentsSliderUndoSnapshot);
        activeSampleSegmentsSliderUndoSnapshot = null;
        activeSampleSegmentsSliderChanged = false;
    }

    private void EndManualAngleSliderUndo(bool pushIfChanged)
    {
        if (activeManualAngleSliderUndoSnapshot != null && pushIfChanged && activeManualAngleSliderChanged && !activeManualAngleSliderUndoPushed)
            state.PushCapturedStructureUndo(activeManualAngleSliderUndoSnapshot);

        activeManualAngleSliderKey = null;
        activeManualAngleSliderUndoSnapshot = null;
        activeManualAngleSliderChanged = false;
        activeManualAngleSliderUndoPushed = false;
    }

    private void DrawAnglePreview(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.09f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, SkyPrisonAnimationWorkbenchStyle.LineColor);

        GUI.Label(new Rect(rect.x + 8f, rect.y + 6f, 220f, 18f), "角度写入预览", EditorStyles.boldLabel);

        List<SkyPrisonAnimationRigRow> rows = state.GetManualAngleTargetRows();
        Rect info = new Rect(rect.x + 12f, rect.y + 34f, rect.width - 24f, 78f);
        EditorGUI.DrawRect(info, new Color(1f, 1f, 1f, 0.035f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(info, new Color(1f, 1f, 1f, 0.05f));

        GUI.Label(new Rect(info.x + 10f, info.y + 8f, info.width - 20f, 18f), "扫描节点：" + rows.Count + " 个", EditorStyles.label);
        GUI.Label(new Rect(info.x + 10f, info.y + 30f, info.width - 20f, 18f), "当前帧：" + state.TimelineCurrentFrame + "    采样帧：" + state.FormatManualAngleSampleFrames(), EditorStyles.miniLabel);
        GUI.Label(new Rect(info.x + 10f, info.y + 52f, info.width - 20f, 18f), "写入字段：runtimeBoneHeadOffset；角度范围：-180° ~ 180°", EditorStyles.miniLabel);

        Rect graph = new Rect(rect.x + 12f, info.yMax + 18f, rect.width - 24f, Mathf.Max(120f, rect.height - info.height - 70f));
        SkyPrisonAnimationWorkbenchStyle.DrawGrid(graph, 20f, new Color(1f, 1f, 1f, 0.035f));
        GUI.Label(new Rect(graph.x + 8f, graph.y + 6f, graph.width - 16f, 18f), "前 12 个节点角度概览", EditorStyles.miniLabel);

        if (Event.current.type != EventType.Repaint)
            return;

        Handles.BeginGUI();
        int count = Mathf.Min(rows.Count, 12);
        if (count > 0)
        {
            for (int i = 0; i < count; i++)
            {
                SkyPrisonAnimationRigRow row = rows[i];
                float angle = state.GetManualBoneAngle(row.key);
                float cx = Mathf.Lerp(graph.x + 20f, graph.xMax - 20f, count <= 1 ? 0f : i / (float)(count - 1));
                float cy = Mathf.Lerp(graph.yMax - 24f, graph.y + 34f, Mathf.InverseLerp(-180f, 180f, angle));
                Handles.color = SkyPrisonAnimationWorkbenchStyle.AccentBlue;
                Handles.DrawSolidDisc(new Vector3(cx, cy, 0f), Vector3.forward, 3.5f);
                Handles.color = new Color(0.25f, 0.55f, 1f, 0.75f);
                Handles.DrawLine(new Vector3(cx, graph.center.y, 0f), new Vector3(cx, cy, 0f));
            }
        }
        Handles.EndGUI();
    }
}
