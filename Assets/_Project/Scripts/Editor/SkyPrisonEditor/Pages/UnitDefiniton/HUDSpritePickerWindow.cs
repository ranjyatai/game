using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class HUDSpritePickerWindow : EditorWindow
{
    private class Entry
    {
        public string assetPath;
        public string displayName;
        public Texture2D texture;
    }

    private static Action<Texture2D> onPick;
    private static string rootFolder = "Assets/_Project/UIUX/HUD";
    private static string titleText = "选择HUD图片";

    private readonly List<Entry> entries = new();
    private Vector2 scroll;
    private string search = "";

    public static void Open(Action<Texture2D> onSelected, string folder = "Assets/_Project/UIUX/HUD", string title = "选择HUD图片")
    {
        onPick = onSelected;
        rootFolder = string.IsNullOrWhiteSpace(folder) ? "Assets/_Project/UIUX/HUD" : folder;
        titleText = string.IsNullOrWhiteSpace(title) ? "选择HUD图片" : title;

        HUDSpritePickerWindow window = CreateInstance<HUDSpritePickerWindow>();
        window.titleContent = new GUIContent(titleText);
        window.minSize = new Vector2(680f, 540f);
        window.RefreshEntries();
        window.ShowAuxWindow();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(6f);
        search = EditorGUILayout.TextField("搜索", search);

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新", GUILayout.Height(22f)))
                RefreshEntries();

            if (GUILayout.Button("打开文件夹", GUILayout.Height(22f)))
                EditorUtility.RevealInFinder(Path.GetFullPath(rootFolder));

            if (GUILayout.Button("清空", GUILayout.Height(22f)))
            {
                onPick?.Invoke(null);
                Close();
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.HelpBox(
            $"读取目录：{rootFolder}\n现在按完整 PNG / Texture 文件读取，不再从切片 Sprite 里猜主图。",
            MessageType.None);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        IEnumerable<Entry> query = entries;
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.displayName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

        foreach (Entry entry in query)
        {
            using (new EditorGUILayout.HorizontalScope("box"))
            {
                Rect previewRect = GUILayoutUtility.GetRect(92f, 40f, GUILayout.Width(92f), GUILayout.Height(40f));
                DrawTexturePreview(previewRect, entry.texture);

                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(entry.displayName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(entry.assetPath, EditorStyles.miniLabel);
                }

                if (GUILayout.Button("选择", GUILayout.Width(64f), GUILayout.Height(24f)))
                {
                    onPick?.Invoke(entry.texture);
                    Close();
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    private void RefreshEntries()
    {
        entries.Clear();
        if (!AssetDatabase.IsValidFolder(rootFolder))
            return;

        string[] textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { rootFolder });
        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                continue;

            entries.Add(new Entry
            {
                assetPath = path,
                displayName = Path.GetFileNameWithoutExtension(path),
                texture = texture
            });
        }

        entries.Sort((a, b) => string.Compare(a.displayName, b.displayName, StringComparison.OrdinalIgnoreCase));
    }

    private void DrawTexturePreview(Rect rect, Texture2D texture)
    {
        EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.14f, 1f));
        if (texture == null)
            return;

        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
    }
}
