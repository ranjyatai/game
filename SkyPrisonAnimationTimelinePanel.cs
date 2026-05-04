using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class SkyPrisonAnimationTimelinePanel
{
    private readonly SkyPrisonAnimationWorkbenchState state;
    private bool draggingPlayhead;
    private string lastSyncedActionKey = "";
    private int lastAutoScrolledFrame = -1;
    private float lastAutoScrolledDensity = -1f;

    private string timelineDurationEditText = "";
    private string timelineFrameRateEditText = "";
    private string playbackSpeedEditText = "";

    private float timelineDurationEditLastValue = float.NaN;
    private int timelineFrameRateEditLastValue = int.MinValue;
    private float playbackSpeedEditLastValue = float.NaN;

    private const string DurationInputControlName = "SkyPrisonTimeline_DurationInput";
    private const string FrameRateInputControlName = "SkyPrisonTimeline_FrameRateInput";
    private const string PlaybackSpeedInputControlName = "SkyPrisonTimeline_PlaybackSpeedInput";

    // Unity IMGUI 的原生 TextField 在这个时间线区域里会被外层 Group / 时间线命中逻辑抢焦点。
    // 这里改成专用的数字输入锁定态：点中以后由本面板直接接管键盘输入。
    private string activeTimelineTextInputControl = "";
    private string pendingTimelineTextInputCommitControl = "";
    private bool activeTimelineTextInputReplaceOnType = false;

    public SkyPrisonAnimationTimelinePanel(SkyPrisonAnimationWorkbenchState state)
    {
        this.state = state;
    }

    public void Draw(Rect rect)
    {
        if (rect.width < 8f || rect.height < 8f)
            return;

        GUI.BeginGroup(rect);
        Rect local = new Rect(0f, 0f, rect.width, rect.height);
        EditorGUI.DrawRect(local, SkyPrisonAnimationWorkbenchStyle.PanelBg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(local, SkyPrisonAnimationWorkbenchStyle.LineColor);
        GUI.Label(new Rect(10f, 6f, 160f, 20f), "时间线", EditorStyles.boldLabel);

        if (state.IsActionGroupSelected())
        {
            DrawGroupSelectedOverlay(local);
            GUI.EndGroup();
            return;
        }

        SyncTimelineToSelectedAction();
        state.SyncActiveTimelineTrackToCurrentSelection(true);
        HandleTimelineShortcuts(rect);

        DrawControls(new Rect(10f, 30f, local.width - 20f, 44f));
        DrawTracks(new Rect(10f, 84f, local.width - 20f, Mathf.Max(86f, local.height - 128f)));
        DrawDensity(new Rect(10f, local.height - 38f, local.width - 20f, 30f));

        GUI.EndGroup();
    }

    private void DrawGroupSelectedOverlay(Rect local)
    {
        Rect box = new Rect(10f, 32f, local.width - 20f, Mathf.Max(72f, local.height - 44f));
        EditorGUI.DrawRect(box, new Color(0.10f, 0.10f, 0.11f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(box, new Color(1f, 1f, 1f, 0.08f));

        string groupName = state.CurrentActionGroupDisplayName();
        GUIStyle title = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            normal = { textColor = new Color(1f, 0.86f, 0.58f, 1f) }
        };
        GUIStyle msg = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            normal = { textColor = new Color(0.78f, 0.78f, 0.80f, 1f) }
        };

        GUI.Label(new Rect(box.x + 16f, box.center.y - 30f, box.width - 32f, 22f), "当前选中动作组：「" + groupName + "」", title);
        GUI.Label(new Rect(box.x + 28f, box.center.y - 4f, box.width - 56f, 42f), "动作组只是分类容器，没有独立时间线。请选择组内具体动作后再编辑骨骼、曲面、Motion 或事件关键帧。", msg);
    }

    private void SyncTimelineToSelectedAction()
    {
        SkyPrisonAnimationActionRow action = state.CurrentAction();
        string key = action != null ? action.key : "";
        if (key == lastSyncedActionKey)
            return;

        lastSyncedActionKey = key;
        if (action != null && action.duration > 0.001f)
        {
            state.TimelineDurationSeconds = action.duration;
            state.TimelineDuration = action.duration;
            state.CurrentTime = Mathf.Clamp(state.CurrentTime, 0f, state.TimelineDurationSeconds);
        }
    }

    private void DrawControls(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(0.10f, 0.10f, 0.11f, 1f));

        float x = r.x + 10f;
        float y = r.y + 9f;
        const float h = 24f;

        GUI.Label(new Rect(x, y + 3f, 52f, h), "轨道秒");
        x += 58f;
        DrawDurationInput(new Rect(x, y, 64f, h));
        x += 82f;

        GUI.Label(new Rect(x, y + 3f, 36f, h), "帧率");
        x += 40f;
        DrawFrameRateInput(new Rect(x, y, 50f, h));
        x += 66f;

        if (IconButton(new Rect(x, y, 28f, h), 27, "回到第0帧"))
            state.SetCurrentFrame(0);
        x += 34f;

        if (IconButton(new Rect(x, y, 28f, h), 40, "回退1帧"))
            state.SetCurrentFrame(state.TimelineCurrentFrame - 1);
        x += 34f;

        if (IconButton(new Rect(x, y, 34f, h), state.PreviewPlaying ? 29 : 26, state.PreviewPlaying ? "暂停" : "播放"))
            state.PreviewPlaying = !state.PreviewPlaying;
        x += 40f;

        if (IconButton(new Rect(x, y, 28f, h), 41, "快进1帧"))
            state.SetCurrentFrame(state.TimelineCurrentFrame + 1);
        x += 34f;

        if (IconButton(new Rect(x, y, 28f, h), 28, "到最后一帧"))
            state.SetCurrentFrame(state.TimelineTotalFrames);
        x += 48f;

        DrawProfessionalTimeReadout(new Rect(x, r.y + 3f, 118f, r.height - 6f));
        x += 126f;

        GUI.Label(new Rect(x, y + 3f, 44f, h), "当前帧");
        x += 48f;
        GUI.Label(new Rect(x, y, 54f, h), FormatFrameCounterLocal(), GetCurrentFrameReadoutStyle());
        x += 64f;

        GUI.Label(new Rect(x, y + 3f, 44f, h), "速度");
        x += 48f;
        DrawPlaybackSpeedInput(new Rect(x, y, 64f, h));
        x += 68f;
        GUI.Label(new Rect(x, y + 3f, 18f, h), "%");
        x += 28f;

        // 旧版“待机/行走/奔跑模板入轨”会生成写死的错误动作，
        // 现在全部停用：动作只能由关键帧、复制粘贴或动作参数生成。
        state.SyncCurrentActionDurationFromTimeline();
    }

    private void DrawDurationInput(Rect rect)
    {
        SyncFloatEditText(
            ref timelineDurationEditText,
            ref timelineDurationEditLastValue,
            state.TimelineDurationSeconds,
            DurationInputControlName,
            "0.###");

        string edited = DrawStableTextInput(rect, DurationInputControlName, timelineDurationEditText);
        if (edited != timelineDurationEditText)
            timelineDurationEditText = edited;

        if (ShouldCommitTextInput(DurationInputControlName))
        {
            if (TryParsePositiveFloat(timelineDurationEditText, out float value))
            {
                float clamped = Mathf.Max(0.01f, value);
                if (!Mathf.Approximately(clamped, state.TimelineDurationSeconds))
                {
                    state.PushStructureUndo();
                    state.TimelineDurationSeconds = clamped;
                    state.TimelineDuration = clamped;
                    SkyPrisonAnimationActionRow action = state.CurrentAction();
                    if (action != null)
                        action.duration = clamped;
                    state.SyncCurrentActionDurationFromTimeline();
                }
                timelineDurationEditLastValue = state.TimelineDurationSeconds;
                timelineDurationEditText = state.TimelineDurationSeconds.ToString("0.###");
            }
            else
            {
                timelineDurationEditText = state.TimelineDurationSeconds.ToString("0.###");
                timelineDurationEditLastValue = state.TimelineDurationSeconds;
            }
        }
    }

    private void DrawFrameRateInput(Rect rect)
    {
        SyncIntEditText(
            ref timelineFrameRateEditText,
            ref timelineFrameRateEditLastValue,
            state.TimelineFrameRate,
            FrameRateInputControlName);

        string edited = DrawStableTextInput(rect, FrameRateInputControlName, timelineFrameRateEditText);
        if (edited != timelineFrameRateEditText)
            timelineFrameRateEditText = edited;

        if (ShouldCommitTextInput(FrameRateInputControlName))
        {
            if (int.TryParse(timelineFrameRateEditText, out int value))
            {
                int clamped = Mathf.Clamp(value, 1, 240);
                if (clamped != state.TimelineFrameRate)
                {
                    state.PushStructureUndo();
                    state.TimelineFrameRate = clamped;
                    state.SyncCurrentActionDurationFromTimeline();
                }
                timelineFrameRateEditLastValue = state.TimelineFrameRate;
                timelineFrameRateEditText = state.TimelineFrameRate.ToString();
            }
            else
            {
                timelineFrameRateEditText = state.TimelineFrameRate.ToString();
                timelineFrameRateEditLastValue = state.TimelineFrameRate;
            }
        }
    }

    private void DrawPlaybackSpeedInput(Rect rect)
    {
        SyncFloatEditText(
            ref playbackSpeedEditText,
            ref playbackSpeedEditLastValue,
            state.PlaybackSpeedPercent,
            PlaybackSpeedInputControlName,
            "0.##");

        string edited = DrawStableTextInput(rect, PlaybackSpeedInputControlName, playbackSpeedEditText);
        if (edited != playbackSpeedEditText)
            playbackSpeedEditText = edited;

        if (ShouldCommitTextInput(PlaybackSpeedInputControlName))
        {
            if (TryParsePositiveFloat(playbackSpeedEditText, out float value))
            {
                float clamped = Mathf.Clamp(value, 1f, 400f);
                if (!Mathf.Approximately(clamped, state.PlaybackSpeedPercent))
                    state.PlaybackSpeedPercent = clamped;

                playbackSpeedEditLastValue = state.PlaybackSpeedPercent;
                playbackSpeedEditText = state.PlaybackSpeedPercent.ToString("0.##");
            }
            else
            {
                playbackSpeedEditText = state.PlaybackSpeedPercent.ToString("0.##");
                playbackSpeedEditLastValue = state.PlaybackSpeedPercent;
            }
        }
    }

    private string DrawStableTextInput(Rect rect, string controlName, string text)
    {
        Event e = Event.current;
        bool isActive = activeTimelineTextInputControl == controlName;
        bool mouseInside = e != null && rect.Contains(e.mousePosition);

        if (e != null && e.type == EventType.MouseDown && e.button == 0)
        {
            if (mouseInside)
            {
                activeTimelineTextInputControl = controlName;
                activeTimelineTextInputReplaceOnType = true;
                pendingTimelineTextInputCommitControl = "";

                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                EditorGUIUtility.editingTextField = false;

                e.Use();
                isActive = true;
            }
            else if (isActive)
            {
                pendingTimelineTextInputCommitControl = controlName;
                activeTimelineTextInputControl = "";
                activeTimelineTextInputReplaceOnType = false;
                e.Use();
                isActive = false;
            }
        }

        if (isActive && e != null && e.type == EventType.KeyDown)
        {
            bool ctrl = e.control || e.command;

            if (e.keyCode == KeyCode.Return ||
                e.keyCode == KeyCode.KeypadEnter ||
                e.keyCode == KeyCode.Tab)
            {
                pendingTimelineTextInputCommitControl = controlName;
                activeTimelineTextInputControl = "";
                activeTimelineTextInputReplaceOnType = false;
                e.Use();
                isActive = false;
            }
            else if (e.keyCode == KeyCode.Escape)
            {
                // Esc 只退出编辑并提交当前可解析值；如果不可解析，上层会自动还原。
                pendingTimelineTextInputCommitControl = controlName;
                activeTimelineTextInputControl = "";
                activeTimelineTextInputReplaceOnType = false;
                e.Use();
                isActive = false;
            }
            else if (ctrl && e.keyCode == KeyCode.A)
            {
                activeTimelineTextInputReplaceOnType = true;
                e.Use();
            }
            else if (e.keyCode == KeyCode.Backspace)
            {
                if (activeTimelineTextInputReplaceOnType)
                {
                    text = "";
                    activeTimelineTextInputReplaceOnType = false;
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    text = text.Substring(0, text.Length - 1);
                }
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete)
            {
                if (activeTimelineTextInputReplaceOnType)
                {
                    text = "";
                    activeTimelineTextInputReplaceOnType = false;
                }
                e.Use();
            }
            else
            {
                char c = e.character;
                if (IsAllowedTimelineNumericChar(c))
                {
                    string ch = c == '，' || c == ',' ? "." : c.ToString();

                    if (activeTimelineTextInputReplaceOnType)
                    {
                        text = ch;
                        activeTimelineTextInputReplaceOnType = false;
                    }
                    else
                    {
                        text += ch;
                    }

                    e.Use();
                }
            }
        }

        DrawTimelineNumericInputVisual(rect, text, isActive, activeTimelineTextInputReplaceOnType);
        return text;
    }

    private void DrawTimelineNumericInputVisual(Rect rect, string text, bool active, bool replaceOnType)
    {
        Color bg = active
            ? new Color(0.19f, 0.19f, 0.20f, 1f)
            : new Color(0.125f, 0.125f, 0.13f, 1f);

        Color border = active
            ? new Color(0.55f, 0.72f, 1f, 1f)
            : new Color(1f, 1f, 1f, 0.18f);

        EditorGUI.DrawRect(rect, bg);
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(rect, border);

        Rect textRect = new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, rect.height - 4f);
        GUIStyle style = new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Clip,
            normal = { textColor = active ? Color.white : new Color(0.82f, 0.82f, 0.84f, 1f) }
        };

        string drawText = text ?? "";

        if (active)
        {
            if (replaceOnType)
            {
                EditorGUI.DrawRect(new Rect(textRect.x - 2f, textRect.y + 3f, Mathf.Min(textRect.width, Mathf.Max(8f, style.CalcSize(new GUIContent(drawText)).x + 4f)), textRect.height - 6f),
                    new Color(0.20f, 0.42f, 0.86f, 0.75f));
            }
            else
            {
                bool caretVisible = ((int)(EditorApplication.timeSinceStartup * 2.0) % 2) == 0;
                if (caretVisible)
                    drawText += "|";
            }
        }

        GUI.Label(textRect, drawText, style);
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Text);
    }

    private bool IsAllowedTimelineNumericChar(char c)
    {
        if (char.IsDigit(c))
            return true;

        return c == '.' || c == ',' || c == '，' || c == '-';
    }

    private void SyncFloatEditText(ref string editText, ref float lastValue, float currentValue, string controlName, string format)
    {
        if (IsFocusedTextInput(controlName))
            return;

        if (string.IsNullOrEmpty(editText) || float.IsNaN(lastValue) || !Mathf.Approximately(lastValue, currentValue))
        {
            editText = currentValue.ToString(format);
            lastValue = currentValue;
        }
    }

    private void SyncIntEditText(ref string editText, ref int lastValue, int currentValue, string controlName)
    {
        if (IsFocusedTextInput(controlName))
            return;

        if (string.IsNullOrEmpty(editText) || lastValue != currentValue)
        {
            editText = currentValue.ToString();
            lastValue = currentValue;
        }
    }

    private bool ShouldCommitTextInput(string controlName)
    {
        if (pendingTimelineTextInputCommitControl == controlName)
        {
            pendingTimelineTextInputCommitControl = "";
            return true;
        }

        return !IsFocusedTextInput(controlName);
    }

    private bool IsTimelineTextInputFocused()
    {
        return !string.IsNullOrEmpty(activeTimelineTextInputControl);
    }

    private bool IsFocusedTextInput(string controlName)
    {
        return activeTimelineTextInputControl == controlName;
    }

    private bool TryParsePositiveFloat(string text, out float value)
    {
        if (float.TryParse(text, out value))
            return true;

        // 兼容数字小键盘或日文输入法偶发的全角/逗号输入。
        string normalized = (text ?? "").Trim().Replace('，', '.').Replace(',', '.');
        return float.TryParse(normalized, out value);
    }

    private void DrawProfessionalTimeReadout(Rect r)
    {
        GUIStyle timeStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 18,
            alignment = TextAnchor.UpperCenter,
            normal = { textColor = new Color(0.92f, 0.92f, 0.94f, 1f) }
        };

        GUIStyle frameStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 12,
            alignment = TextAnchor.LowerCenter,
            normal = { textColor = new Color(0.82f, 0.82f, 0.84f, 1f) }
        };

        GUI.Label(new Rect(r.x, r.y, r.width, 24f), FormatTimecodeLocal(), timeStyle);
        GUI.Label(new Rect(r.x, r.y + 21f, r.width, 18f), FormatFrameCounterLocal(), frameStyle);
    }

    private string FormatTimecodeLocal()
    {
        int frameRate = state != null ? Mathf.Max(1, state.TimelineFrameRate) : 60;
        int totalFrames = state != null ? Mathf.Max(0, state.TimelineCurrentFrame) : 0;

        // 上方读数显示真实经过时间：分:秒:百分秒。
        // 下方小字另行显示累计帧数，避免把 48 帧误显示成 00:00:48 时间。
        float totalSeconds = totalFrames / (float)frameRate;
        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds) % 60;
        int centiseconds = Mathf.FloorToInt((totalSeconds - Mathf.Floor(totalSeconds)) * 100f + 0.0001f);

        return string.Format("{0:00}:{1:00}:{2:00}", minutes, seconds, centiseconds);
    }

    private GUIStyle GetCurrentFrameReadoutStyle()
    {
        return new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 13,
            normal = { textColor = new Color(0.88f, 0.88f, 0.90f, 1f) }
        };
    }

    private string FormatFrameCounterLocal()
    {
        int frame = state != null ? state.TimelineCurrentFrame : 0;
        return frame.ToString("00000");
    }

    private bool IconButton(Rect r, int icon, string tip)
    {
        Texture2D t = SkyPrisonAnimationWorkbenchStyle.LoadEditorIcon(icon);
        return GUI.Button(r, new GUIContent(t, tip));
    }

    private void DrawTracks(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.13f, 1f));
        SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(r, SkyPrisonAnimationWorkbenchStyle.LineColor);

        const float labelW = 108f;
        const float rowH = 28f;
        const float timeH = 28f;
        const float hbar = 16f;
        const float vbar = 16f;

        Rect view = new Rect(r.x, r.y, Mathf.Max(80f, r.width - vbar), Mathf.Max(60f, r.height - hbar));

        float pixelsPerFrame = Mathf.Clamp(10f * Mathf.Max(0.15f, state.TimelineDensityZoom), 2f, 80f);
        float validW = state.TimelineTotalFrames * pixelsPerFrame;
        float viewportTrackW = Mathf.Max(80f, view.width - labelW);

        float extraTailW = Mathf.Max(240f, viewportTrackW * 0.35f);
        float contentTrackW = Mathf.Max(viewportTrackW, validW + extraTailW);

        List<string> trackKeys = state.GetTimelineTrackKeysForCurrentAction();
        if (trackKeys.Count == 0)
        {
            GUI.Label(new Rect(r.x + 12f, r.y + 34f, r.width - 24f, 22f), "左侧选择 Rig 骨骼节点后，这里会显示对应轨道。PSB 图层不会进入骨骼时间轴。", EditorStyles.miniLabel);
        }

        int rowCount = Mathf.Max(1, trackKeys.Count);
        float contentH = timeH + rowCount * rowH + 18f;
        Rect content = new Rect(0f, 0f, labelW + contentTrackW, contentH);

        EnsurePlayheadVisible(view, content, labelW, pixelsPerFrame);

        state.TimelineScroll = GUI.BeginScrollView(view, state.TimelineScroll, content, true, true);

        Rect gridRect = new Rect(labelW, 0f, contentTrackW, contentH);
        DrawFrameGrid(gridRect, pixelsPerFrame);

        float y = timeH + 8f;
        for (int i = 0; i < trackKeys.Count; i++)
        {
            Color c = GetTrackColor(i, trackKeys[i]);
            DrawTimelineKeyRow(labelW, y, rowH, trackKeys[i], state.GetTimelineTrackLabel(trackKeys[i]), c, pixelsPerFrame, contentTrackW);
            y += rowH;
        }

        DrawUnavailableArea(gridRect, validW);

        float playX = labelW + state.TimelineCurrentFrameFloat * pixelsPerFrame;
        EditorGUI.DrawRect(new Rect(playX, 0f, 2f, contentH), Color.white);
        HandlePlayhead(new Rect(labelW, 0f, contentTrackW, contentH), pixelsPerFrame);

        GUI.EndScrollView();
    }

    private void EnsurePlayheadVisible(Rect view, Rect content, float labelW, float ppf)
    {
        int frame = state.TimelineCurrentFrame;
        bool frameChanged = frame != lastAutoScrolledFrame;
        bool densityChanged = !Mathf.Approximately(state.TimelineDensityZoom, lastAutoScrolledDensity);
        if (!frameChanged && !densityChanged)
            return;

        float playX = labelW + state.TimelineCurrentFrameFloat * ppf;
        float left = state.TimelineScroll.x;
        float right = state.TimelineScroll.x + view.width;
        float preferredLeft = Mathf.Max(0f, playX - labelW - 10f);
        float maxScrollX = Mathf.Max(0f, content.width - view.width);

        // 播放头跑出当前可见轨道区域时，横向滚动条自动追上。
        // 追上后让白线落在左侧轨道起点附近，而不是停在最右边才继续播放。
        if (playX > right - 28f || playX < left + labelW + 10f)
        {
            state.TimelineScroll.x = Mathf.Clamp(preferredLeft, 0f, maxScrollX);
        }

        lastAutoScrolledFrame = frame;
        lastAutoScrolledDensity = state.TimelineDensityZoom;
    }

    private void DrawTimelineKeyRow(float labelW, float y, float h, string targetKey, string label, Color c, float ppf, float w)
    {
        DrawTrackBase(labelW, y, h, label, w, out Rect track);

        bool isFootstepTrack = state.IsFootstepTimelineTrack(targetKey);
        bool activeTrack = state.ActiveTimelineTrackKey == targetKey;
        if (activeTrack)
        {
            SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(new Rect(0f, y + 1f, labelW + w - 8f, h - 2f), SkyPrisonAnimationWorkbenchStyle.AccentBlue);
            EditorGUI.DrawRect(new Rect(0f, y + 1f, 3f, h - 2f), SkyPrisonAnimationWorkbenchStyle.AccentBlue);
        }

        if (state.IsMotionTimelineTrack(targetKey))
        {
            DrawMotionKeyRow(track, c, ppf);
            HandleMotionTrackInput(track, ppf, targetKey);
            return;
        }

        string actionKey = state.CurrentActionKey();
        for (int i = 0; i < state.TimelineKeyframes.Count; i++)
        {
            SkyPrisonAnimationTimelineKeyframe k = state.TimelineKeyframes[i];
            if (k == null || k.actionKey != actionKey || k.targetKey != targetKey)
                continue;

            float x = track.x + state.SnapFrame(k.frame) * ppf;
            if (x < track.x - 10f || x > track.xMax + 10f)
                continue;

            Rect keyRect = new Rect(x - 6f, track.center.y - 6f, 12f, 12f);
            Rect pickRect = new Rect(x - 10f, track.center.y - 10f, 20f, 20f);
            bool selected = state.SelectedTimelineKeyframeIndex == i;

            // 模板关键帧要在轨道上“明确可见”。
            // 脚步声轨道是给音声系统用的事件标志，用窄竖线 + 小方块和姿势关键帧区分。
            Color keyColor = selected ? Color.white : c;
            if (isFootstepTrack)
            {
                EditorGUI.DrawRect(new Rect(x - 1f, track.y - 2f, 2f, track.height + 4f), keyColor);
                EditorGUI.DrawRect(keyRect, keyColor);
            }
            else
            {
                EditorGUI.DrawRect(keyRect, keyColor);
            }
            SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(keyRect, new Color(0f, 0f, 0f, 0.55f));
            if (selected)
                SkyPrisonAnimationWorkbenchStyle.DrawRectBorder(new Rect(keyRect.x - 2f, keyRect.y - 2f, keyRect.width + 4f, keyRect.height + 4f), c);

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && pickRect.Contains(e.mousePosition))
            {
                state.SelectedTimelineKeyframeIndex = i;
                state.SelectTimelineTrack(targetKey, true);
                state.SetCurrentFrame(k.frame);
                state.PreviewPlaying = false;
                GUI.FocusControl(null);
                e.Use();
            }
        }

        Event ev = Event.current;
        if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && track.Contains(ev.mousePosition))
        {
            int f = Mathf.RoundToInt((ev.mousePosition.x - track.x) / Mathf.Max(0.001f, ppf));

            // 左键只做“选择轨道 + 锁定编辑对象 + 移动播放头”。
            // 关键帧必须通过右键菜单显式插入，避免误点轨道就产生关键帧。
            // 同时清掉旧的关键帧选中态，避免看起来像是左键点轨道生成了一个新关键帧。
            state.SelectTimelineTrack(targetKey, true);
            state.SelectedTimelineKeyframeIndex = -1;
            state.SetCurrentFrame(f);
            state.PreviewPlaying = false;
            GUI.FocusControl(null);
            ev.Use();
        }
        else if (ev != null && ev.type == EventType.ContextClick && track.Contains(ev.mousePosition))
        {
            int f = Mathf.RoundToInt((ev.mousePosition.x - track.x) / Mathf.Max(0.001f, ppf));
            state.SelectTimelineTrack(targetKey, true);
            state.SetCurrentFrame(f);
            ShowTimelineContextMenu(targetKey);
            ev.Use();
        }
    }

    private void DrawMotionKeyRow(Rect track, Color c, float ppf)
    {
        string actionKey = state.CurrentActionKey();
        // Motion 轨道用连线 + 菱形，区别于普通骨骼关键帧。
        Vector2 last = Vector2.zero;
        bool hasLast = false;
        for (int i = 0; i < state.MotionKeyframes.Count; i++)
        {
            SkyPrisonAnimationMotionKeyframe k = state.MotionKeyframes[i];
            if (k == null || !string.Equals(k.actionKey, actionKey, System.StringComparison.OrdinalIgnoreCase))
                continue;
            float x = track.x + state.SnapFrame(k.frame) * ppf;
            if (x < track.x - 10f || x > track.xMax + 10f)
                continue;
            Vector2 p = new Vector2(x, track.center.y);
            if (hasLast)
                SkyPrisonAnimationWorkbenchStyle.DrawLine(last, p, new Color(c.r, c.g, c.b, 0.65f), 2f);
            hasLast = true;
            last = p;
            Rect keyRect = new Rect(x - 6f, track.center.y - 6f, 12f, 12f);
            bool selected = state.SelectedMotionKeyframeIndex == i;
            Color keyColor = selected ? Color.white : c;
            Vector3[] diamond = {
                new Vector3(keyRect.center.x, keyRect.yMin, 0f),
                new Vector3(keyRect.xMax, keyRect.center.y, 0f),
                new Vector3(keyRect.center.x, keyRect.yMax, 0f),
                new Vector3(keyRect.xMin, keyRect.center.y, 0f),
                new Vector3(keyRect.center.x, keyRect.yMin, 0f)
            };
            Handles.BeginGUI();
            Handles.color = keyColor;
            Handles.DrawAAPolyLine(3f, diamond);
            Handles.EndGUI();
            Rect pickRect = new Rect(x - 10f, track.center.y - 10f, 20f, 20f);
            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && pickRect.Contains(e.mousePosition))
            {
                state.SelectedMotionKeyframeIndex = i;
                state.SelectedTimelineKeyframeIndex = -1;
                state.SelectTimelineTrack(SkyPrisonAnimationWorkbenchState.MotionTimelineTrackKey, true);
                state.SetCurrentFrame(k.frame);
                state.PreviewPlaying = false;
                GUI.FocusControl(null);
                e.Use();
            }
        }
    }

    private void HandleMotionTrackInput(Rect track, float ppf, string targetKey)
    {
        if (!state.CanEditCurrentActionTimeline()) return;
        Event ev = Event.current;
        if (ev != null && ev.type == EventType.MouseDown && ev.button == 0 && track.Contains(ev.mousePosition))
        {
            int f = Mathf.RoundToInt((ev.mousePosition.x - track.x) / Mathf.Max(0.001f, ppf));
            state.SelectTimelineTrack(targetKey, true);
            state.SelectedTimelineKeyframeIndex = -1;
            state.SelectedMotionKeyframeIndex = -1;
            state.SetCurrentFrame(f);
            state.PreviewPlaying = false;
            GUI.FocusControl(null);
            ev.Use();
        }
        else if (ev != null && ev.type == EventType.ContextClick && track.Contains(ev.mousePosition))
        {
            int f = Mathf.RoundToInt((ev.mousePosition.x - track.x) / Mathf.Max(0.001f, ppf));
            state.SelectTimelineTrack(targetKey, true);
            state.SetCurrentFrame(f);
            ShowTimelineContextMenu(targetKey);
            ev.Use();
        }
    }

    private Color GetTrackColor(int index, string key)
    {
        if (state != null && state.IsMotionTimelineTrack(key))
            return new Color(0.35f, 0.68f, 1f, 1f);
        if (state != null && state.IsFootstepTimelineTrack(key))
            return new Color(0.35f, 0.90f, 0.55f, 1f);

        if (key != null)
        {
            if (key.Contains("Head")) return SkyPrisonAnimationWorkbenchStyle.AccentGreen;
            if (key.Contains("Arm") || key.Contains("Hand") || key.Contains("Wrist")) return SkyPrisonAnimationWorkbenchStyle.AccentYellow;
            if (key.Contains("Foot") || key.Contains("Leg") || key.Contains("Ankle")) return SkyPrisonAnimationWorkbenchStyle.AccentPurple;
        }
        Color[] colors = { SkyPrisonAnimationWorkbenchStyle.AccentBlue, SkyPrisonAnimationWorkbenchStyle.AccentGreen, SkyPrisonAnimationWorkbenchStyle.AccentYellow, SkyPrisonAnimationWorkbenchStyle.AccentPurple };
        return colors[Mathf.Abs(index) % colors.Length];
    }

    private void DrawFrameGrid(Rect r, float ppf)
    {
        int totalVisibleFrames = Mathf.CeilToInt(r.width / Mathf.Max(1f, ppf));

        int gridStep = 1;
        if (ppf < 3f) gridStep = 20;
        else if (ppf < 5f) gridStep = 10;
        else if (ppf < 8f) gridStep = 5;
        else if (ppf < 14f) gridStep = 2;

        int labelStep = gridStep;
        while (labelStep * ppf < 54f)
            labelStep *= 2;

        int fps = Mathf.Max(1, state.TimelineFrameRate);

        for (int f = 0; f <= totalVisibleFrames; f += gridStep)
        {
            float x = r.x + f * ppf;
            bool secondLine = f % fps == 0;
            bool labelLine = f % labelStep == 0;
            Color c = secondLine ? new Color(1f, 1f, 1f, 0.22f) : (labelLine ? new Color(1f, 1f, 1f, 0.13f) : new Color(1f, 1f, 1f, 0.07f));
            EditorGUI.DrawRect(new Rect(x, r.y, 1f, r.height), c);
        }

        for (int f = 0; f <= totalVisibleFrames; f += labelStep)
        {
            float x = r.x + f * ppf;
            GUI.Label(new Rect(x + 3f, r.y + 3f, 72f, 18f), FormatFrameLabel(f), EditorStyles.miniLabel);
        }
    }

    private void DrawUnavailableArea(Rect r, float validW)
    {
        float disabledX = r.x + validW;
        if (disabledX >= r.xMax - 1f)
            return;

        Rect disabled = new Rect(disabledX, r.y, r.xMax - disabledX, r.height);
        EditorGUI.DrawRect(disabled, new Color(1f, 1f, 1f, 0.13f));
        EditorGUI.DrawRect(new Rect(disabledX, r.y, 2f, r.height), new Color(1f, 1f, 1f, 0.30f));
    }

    private string FormatFrameLabel(int f)
    {
        int fps = Mathf.Max(1, state.TimelineFrameRate);
        float seconds = Mathf.Max(0f, f / (float)fps);

        // Timeline ruler labels must be shown as real seconds, not "second:frame".
        // Example at 60fps:
        // 72 frames = 1.20s, not "1:12"
        // 48 frames = 0.80s, not "0:48"
        if (seconds < 10f)
            return seconds.ToString("0.00") + "s";

        int minutes = Mathf.FloorToInt(seconds / 60f);
        float sec = seconds - minutes * 60f;
        return string.Format("{0}:{1:00.00}s", minutes, sec);
    }

    private void DrawTrackBase(float labelW, float y, float h, string label, float w, out Rect track)
    {
        GUI.Label(new Rect(0f, y + 4f, labelW - 6f, h), label);
        track = new Rect(labelW, y + 5f, Mathf.Max(20f, w - 8f), 16f);
        EditorGUI.DrawRect(track, new Color(0.06f, 0.06f, 0.07f, 1f));
    }

    private void HandlePlayhead(Rect area, float ppf)
    {
        if (!state.CanEditCurrentActionTimeline()) return;
        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDown && e.button == 0 && area.Contains(e.mousePosition))
        {
            draggingPlayhead = true;
            SetPlayheadFromMouse(area, ppf, e.mousePosition.x);
            e.Use();
        }
        else if (e.type == EventType.MouseDrag && draggingPlayhead)
        {
            SetPlayheadFromMouse(area, ppf, e.mousePosition.x);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && draggingPlayhead)
        {
            draggingPlayhead = false;
            e.Use();
        }
    }

    private void SetPlayheadFromMouse(Rect area, float ppf, float mouseX)
    {
        int f = Mathf.RoundToInt((mouseX - area.x) / Mathf.Max(0.001f, ppf));
        state.SetCurrentFrame(f);
        state.PreviewPlaying = false;
    }

    private void DrawDensity(Rect r)
    {
        EditorGUI.DrawRect(r, new Color(0.10f, 0.10f, 0.11f, 1f));

        float x = r.x + 10f;
        GUI.Label(new Rect(x, r.y + 7f, 80f, 18f), "时间线密度");
        x += 88f;

        if (GUI.Button(new Rect(x, r.y + 5f, 28f, 20f), "-"))
            state.TimelineDensityZoom = Mathf.Max(0.15f, state.TimelineDensityZoom * 0.8f);
        x += 36f;

        state.TimelineDensityZoom = GUI.HorizontalSlider(new Rect(x, r.y + 10f, 140f, 16f), state.TimelineDensityZoom, 0.15f, 4f);
        x += 150f;

        if (GUI.Button(new Rect(x, r.y + 5f, 28f, 20f), "+"))
            state.TimelineDensityZoom = Mathf.Min(4f, state.TimelineDensityZoom * 1.25f);
        x += 40f;

        GUI.Label(new Rect(x, r.y + 7f, 55f, 18f), Mathf.RoundToInt(state.TimelineDensityZoom * 100f) + "%");
        x += 64f;

        if (GUI.Button(new Rect(x, r.y + 5f, 54f, 20f), "重置"))
            state.ResetTimelineDensity();
        x += 64f;

        // 最右侧：显示全部轨道开关。关闭时，时间线只跟随左侧当前选择刷新。
        Rect allTrackRect = new Rect(r.xMax - 118f, r.y + 5f, 108f, 20f);
        EditorGUI.BeginChangeCheck();
        state.ShowAllTimelineTracks = GUI.Toggle(allTrackRect, state.ShowAllTimelineTracks, "显示全部轨道");
        if (EditorGUI.EndChangeCheck())
        {
            state.SelectedTimelineKeyframeIndex = -1;
            state.SyncActiveTimelineTrackToCurrentSelection(true);
            GUI.changed = true;
        }

        state.TimelineTrackLockEnabled = GUI.Toggle(new Rect(x, r.y + 5f, 64f, 20f), state.TimelineTrackLockEnabled, "锁轨道");
        x += 70f;

        string activeLabel = string.IsNullOrEmpty(state.ActiveTimelineTrackKey) ? "未选轨" : state.GetTimelineTrackLabel(state.ActiveTimelineTrackKey);
        GUI.Label(new Rect(x, r.y + 7f, 78f, 18f), "当前:" + activeLabel, EditorStyles.miniLabel);
        x += 84f;

        GUI.Label(new Rect(x, r.y + 7f, 86f, 18f), "整帧操作", EditorStyles.miniLabel);
        x += 92f;

        using (new EditorGUI.DisabledScope(state.CountCurrentFrameKeyframes() <= 0))
        {
            if (GUI.Button(new Rect(x, r.y + 5f, 54f, 20f), "复制帧"))
                CopyCurrentFrameWithUndo();
            x += 60f;

            if (GUI.Button(new Rect(x, r.y + 5f, 54f, 20f), "删帧"))
                DeleteCurrentFrameWithUndo();
            x += 60f;
        }

        using (new EditorGUI.DisabledScope((state.TimelineFrameClipboard == null || state.TimelineFrameClipboard.Count <= 0) && (state.MotionFrameClipboard == null || state.MotionFrameClipboard.Count <= 0)))
        {
            if (GUI.Button(new Rect(x, r.y + 5f, 54f, 20f), "粘贴帧"))
                PasteCurrentFrameWithUndo();
            x += 60f;
        }

        GUI.Label(new Rect(x, r.y + 7f, 72f, 18f), "单Key", EditorStyles.miniLabel);
        x += 50f;

        using (new EditorGUI.DisabledScope(!state.HasSelectedOrActiveTimelineKeyframe()))
        {
            if (GUI.Button(new Rect(x, r.y + 5f, 42f, 20f), "删"))
                DeleteKeyframeWithUndo();
            x += 48f;

            if (GUI.Button(new Rect(x, r.y + 5f, 42f, 20f), "复制"))
                state.CopySelectedTimelineKeyframe();
            x += 48f;

            if (GUI.Button(new Rect(x, r.y + 5f, 42f, 20f), "剪切"))
                CutKeyframeWithUndo();
            x += 48f;
        }

        using (new EditorGUI.DisabledScope(!state.HasTimelineKeyframeClipboard()))
        {
            if (GUI.Button(new Rect(x, r.y + 5f, 42f, 20f), "粘贴"))
                PasteKeyframeWithUndo();
        }
    }

    private void InsertKeyframeWithUndo()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        List<SkyPrisonAnimationTimelineKeyframe> keys = new List<SkyPrisonAnimationTimelineKeyframe>();
        if (state.TimelineTrackLockEnabled && !string.IsNullOrEmpty(state.ActiveTimelineTrackKey))
        {
            SkyPrisonAnimationTimelineKeyframe k = state.InsertOrUpdateTimelineKeyframeForActiveTrack();
            if (k != null) keys.Add(k);
        }
        else
        {
            keys = state.InsertOrUpdateTimelineKeyframesForSelectedRows();
        }
        if (keys != null && keys.Count > 0)
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
        }
    }

    private void InsertKeyframeForTargetWithUndo(string targetKey)
    {
        object snapshot = state.CaptureStructureUndoSnapshot();

        if (state.IsMotionTimelineTrack(targetKey))
        {
            state.SelectTimelineTrack(targetKey, false);
            SkyPrisonAnimationMotionKeyframe marker = state.InsertOrUpdateMotionKeyframe(state.TimelineCurrentFrame, state.EvaluateMotionVisualOffset());
            if (marker != null)
            {
                state.PushCapturedStructureUndo(snapshot);
                GUI.changed = true;
            }
            return;
        }

        if (state.IsFootstepTimelineTrack(targetKey))
        {
            state.SelectTimelineTrack(targetKey, false);
            SkyPrisonAnimationTimelineKeyframe marker = state.InsertOrUpdateFootstepMarker(state.TimelineCurrentFrame);
            if (marker != null)
            {
                state.PushCapturedStructureUndo(snapshot);
                GUI.changed = true;
            }
            return;
        }

        SkyPrisonAnimationRigRow row = state.FindAnyStructureRow(targetKey);
        if (row == null)
            return;

        state.SelectTimelineTrack(targetKey, true);
        SkyPrisonAnimationTimelineKeyframe k = state.InsertOrUpdateTimelineKeyframe(row, state.TimelineCurrentFrame);
        if (k != null)
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
        }
    }

    private void DeleteKeyframeWithUndo()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        if (state.DeleteSelectedOrActiveTimelineKeyframe())
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
        }
    }

    private bool DeleteKeyframeWithUndoFromShortcut()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        if (state.DeleteSelectedOrActiveTimelineKeyframe())
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
            return true;
        }
        return false;
    }

    private bool CutKeyframeWithUndoFromShortcut()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        if (state.CutSelectedOrActiveTimelineKeyframe())
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
            return true;
        }
        return false;
    }

    private void CutKeyframeWithUndo()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        if (state.CutSelectedOrActiveTimelineKeyframe())
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
        }
    }

    private void PasteKeyframeWithUndo()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        if (state.PasteTimelineKeyframeAtCurrentFrame())
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
        }
    }

    private void CopyCurrentFrameWithUndo()
    {
        if (state.CopyCurrentFrameKeyframes())
            GUI.changed = true;
    }

    private void PasteCurrentFrameWithUndo()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        if (state.PasteCurrentFrameKeyframes())
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
        }
    }

    private void DeleteCurrentFrameWithUndo()
    {
        object snapshot = state.CaptureStructureUndoSnapshot();
        if (state.DeleteCurrentFrameKeyframes() > 0)
        {
            state.PushCapturedStructureUndo(snapshot);
            GUI.changed = true;
        }
    }

    private void ShowTimelineContextMenu(string targetKey)
    {
        GenericMenu menu = new GenericMenu();
        bool footstepTrack = state.IsFootstepTimelineTrack(targetKey);
        bool motionTrack = state.IsMotionTimelineTrack(targetKey);
        string insertLabel = motionTrack ? "添加 / 更新 Motion 关键帧" : (footstepTrack ? "添加 / 更新脚步声标志" : "插入 / 更新关键帧");
        menu.AddItem(new GUIContent(insertLabel), false, delegate
        {
            InsertKeyframeForTargetWithUndo(targetKey);
        });
        if (state.CountCurrentFrameKeyframes() > 0)
        {
            menu.AddItem(new GUIContent("整帧/复制当前帧所有关键帧"), false, CopyCurrentFrameWithUndo);
            menu.AddItem(new GUIContent("整帧/删除当前帧所有关键帧"), false, DeleteCurrentFrameWithUndo);
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("整帧/复制当前帧所有关键帧"));
            menu.AddDisabledItem(new GUIContent("整帧/删除当前帧所有关键帧"));
        }

        if ((state.TimelineFrameClipboard != null && state.TimelineFrameClipboard.Count > 0) || (state.MotionFrameClipboard != null && state.MotionFrameClipboard.Count > 0))
            menu.AddItem(new GUIContent("整帧/粘贴到当前帧"), false, PasteCurrentFrameWithUndo);
        else
            menu.AddDisabledItem(new GUIContent("整帧/粘贴到当前帧"));

        menu.AddSeparator("");

        if (state.HasSelectedOrActiveTimelineKeyframe())
        {
            menu.AddItem(new GUIContent("删除选中关键帧"), false, DeleteKeyframeWithUndo);
            menu.AddItem(new GUIContent("复制选中关键帧"), false, delegate { state.CopySelectedTimelineKeyframe(); });
            menu.AddItem(new GUIContent("剪切选中关键帧"), false, CutKeyframeWithUndo);
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("删除选中关键帧"));
            menu.AddDisabledItem(new GUIContent("复制选中关键帧"));
        }
        if (state.HasTimelineKeyframeClipboard())
            menu.AddItem(new GUIContent("粘贴到当前帧"), false, PasteKeyframeWithUndo);
        else
            menu.AddDisabledItem(new GUIContent("粘贴到当前帧"));
        menu.ShowAsContext();
    }

    private void HandleTimelineShortcuts(Rect rect)
    {
        if (!state.CanEditCurrentActionTimeline()) return;
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown)
            return;

        if (IsTimelineTextInputFocused())
            return;

        if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
        {
            if (DeleteKeyframeWithUndoFromShortcut())
                e.Use();
            return;
        }

        bool ctrl = e.control || e.command;
        if (!ctrl)
            return;

        if (e.keyCode == KeyCode.K)
        {
            // Ctrl/Command + K 不再创建关键帧。
            // 关键帧创建必须走轨道右键菜单，防止快捷键在错误轨道上误写数据。
            e.Use();
        }
        else if (e.keyCode == KeyCode.C)
        {
            if (state.CopySelectedTimelineKeyframe())
                e.Use();
        }
        else if (e.keyCode == KeyCode.X)
        {
            if (CutKeyframeWithUndoFromShortcut())
                e.Use();
        }
        else if (e.keyCode == KeyCode.V)
        {
            PasteKeyframeWithUndo();
            e.Use();
        }
    }
}
