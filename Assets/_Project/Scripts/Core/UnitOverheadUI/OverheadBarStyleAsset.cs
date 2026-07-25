using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[Serializable]
public class OverheadBarBackgroundLayer
{
    public string layerName = "BG Layer";
    public Texture2D texture;
    public bool useTint = true;
    public Color tint = Color.white;
    public Vector2 offset = Vector2.zero;
    public Vector2 sizeOverride = Vector2.zero;
    public float rotationZ = 0f;
}

[CreateAssetMenu(menuName = "SkyPrison/UI/Overhead Bar Style", fileName = "OBS_NewStyle")]
public class OverheadBarStyleAsset : ScriptableObject
{
    public static event Action<OverheadBarStyleAsset> OnStyleChanged;

    public static void NotifyStyleChanged(OverheadBarStyleAsset style)
    {
        if (style == null)
            return;

        OnStyleChanged?.Invoke(style);
    }

    public string styleKey = "Default";
    public string displayName = "默认样式";

    public TMP_FontAsset nameFontAsset;
    public int nameFontSize = 13;
    public Color nameColor = Color.white;

    public List<OverheadBarBackgroundLayer> backgroundLayers = new();

    public Texture2D fillTexture;
    public Texture2D damageReferenceTexture;
    public Color fillColor = Color.white;
    public Color damageReferenceColor = new Color(0.42f, 0.12f, 0.12f, 1f);

    public Vector2 barSize = new Vector2(150f, 14f);
    public Vector2 hpBarOffset = Vector2.zero;
    public Vector2 nameOffset = new Vector2(0f, 16f);
    public float rotationZ = 0f;

    public float paddingLeft = 0f;
    public float paddingRight = 0f;
    public float paddingTop = 0f;
    public float paddingBottom = 0f;

    public bool hideWhenFull = true;
    public float fullHpThreshold = 0.999f;
    public float fadeSpeed = 8f;

    public float fillLerpSpeed = 5f;
    public float damageReferenceHoldTime = 0.35f;
    public float damageReferenceFadeSpeed = 2.5f;

    [Header("状态区域（容器）")]
    public bool enableStatusArea = true;
    public string statusDisplayKey = "default_status_display";
    public Vector2 statusAreaOffset = new Vector2(0f, -22f);
    public Vector2 statusAreaSize = new Vector2(120f, 20f);
    public Vector2 statusIconSize = new Vector2(16f, 16f);
    public Vector2 statusIconSpacing = new Vector2(2f, 2f);
    public TextAnchor statusAreaAlignment = TextAnchor.MiddleCenter;

    [Header("伤害跳字")]
    public bool enableDamageNumbers = true;
    public TMP_FontAsset damageNumberFontAsset;

#if UNITY_EDITOR
    private void OnValidate()
    {
        NotifyStyleChanged(this);
    }
#endif
}