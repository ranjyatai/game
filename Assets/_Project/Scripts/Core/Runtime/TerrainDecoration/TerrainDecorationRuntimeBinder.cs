using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TerrainDecorationMaterialOverride
{
    public string slotId = "";
    public Material material;
}

[DisallowMultipleComponent]
public class TerrainDecorationRuntimeBinder : MonoBehaviour
{
    [Header("Definition")]
    public TerrainDecorationDefinition definition;
    public string instanceId = "";
    public string selectedVariantId = "";
    public int randomSeed = 0;

    [Header("Instance Transform Result")]
    public Vector3 placementEuler = Vector3.zero;
    public Vector3 visualLocalEuler = Vector3.zero;
    public Vector3 finalScale = Vector3.one;

    [Header("Material Result")]
    public List<TerrainDecorationMaterialOverride> materialOverrides = new List<TerrainDecorationMaterialOverride>();

    [Header("Optional Overrides")]
    public bool overrideCollision = false;
    public Vector3 collisionSizeOverride = Vector3.one;
    public Vector3 collisionOffsetOverride = Vector3.zero;

    public TerrainDecorationVariant GetSelectedVariant()
    {
        if (definition == null || definition.variants == null || definition.variants.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(selectedVariantId))
        {
            for (int i = 0; i < definition.variants.Count; i++)
            {
                TerrainDecorationVariant v = definition.variants[i];
                if (v != null && v.variantId == selectedVariantId)
                    return v;
            }
        }

        return definition.variants[0];
    }

    public Material GetMaterialOverride(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId) || materialOverrides == null)
            return null;

        for (int i = 0; i < materialOverrides.Count; i++)
        {
            TerrainDecorationMaterialOverride item = materialOverrides[i];
            if (item != null && item.slotId == slotId && item.material != null)
                return item.material;
        }

        return null;
    }
}
