using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 天空囚笼镜头景深控制器。
///
/// 运行时只保存和应用轻量参数；真正的 URP DepthOfField 通过 VolumeProfile 反射写入，
/// 避免 Core 层直接依赖 Universal 命名空间，方便编辑器和运行时共用。
/// </summary>
[DisallowMultipleComponent]
public class SkyPrisonCameraDepthOfFieldController : MonoBehaviour
{
    [Header("Depth Of Field")]
    public bool enableDepthOfField = false;

    [Tooltip("清晰区域结束距离。超过这个距离的远处开始模糊。")]
    [Range(0.1f, 50f)]
    public float focusDistance = 8f;

    [Tooltip("远处模糊强度。0 为关闭，1 为较强。")]
    [Range(0f, 1f)]
    public float blurStrength = 0.4f;

    [Tooltip("从焦点距离到完全模糊的过渡范围。")]
    [Range(1f, 80f)]
    public float farBlurRange = 18f;

    [Tooltip("编辑预览 / 运行预览建议关闭；正式发布档可开启。")]
    public bool highQualitySampling = false;

    [Header("Binding")]
    public Volume targetVolume;
    public bool applyOnEnable = true;

    private void OnEnable()
    {
        if (applyOnEnable)
            ApplyToVolume();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        focusDistance = Mathf.Clamp(focusDistance, 0.1f, 50f);
        blurStrength = Mathf.Clamp01(blurStrength);
        farBlurRange = Mathf.Clamp(farBlurRange, 1f, 80f);
    }
#endif

    public void ApplyFromMap(MapDefinition map)
    {
        if (map == null)
            return;

        enableDepthOfField = map.enableDepthOfField;
        focusDistance = map.focusDistance;
        blurStrength = map.blurStrength;
        ApplyToVolume();
    }

    public void ApplyToVolume()
    {
        Volume volume = ResolveVolume();
        if (volume == null)
            return;

        targetVolume = volume;
        if (volume.profile == null)
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

        ApplyUniversalDepthOfField(volume.profile);
    }

    private Volume ResolveVolume()
    {
        if (targetVolume != null)
            return targetVolume;

        GameObject named = GameObject.Find("CameraPostProcessVolume");
        if (named != null)
        {
            Volume v = named.GetComponent<Volume>();
            if (v != null)
                return v;
        }

        return UnityEngine.Object.FindFirstObjectByType<Volume>();
    }

    private void ApplyUniversalDepthOfField(VolumeProfile profile)
    {
        if (profile == null)
            return;

        Type dofType = FindUniversalDepthOfFieldType();
        if (dofType == null)
        {
            Debug.LogWarning("[SkyPrisonCameraDepthOfFieldController] 未找到 URP DepthOfField 类型。请确认项目使用 URP，并安装 Universal Render Pipeline。", this);
            return;
        }

        VolumeComponent dof = GetOrAddVolumeComponent(profile, dofType);
        if (dof == null)
            return;

        dof.active = enableDepthOfField && blurStrength > 0.001f;

        // URP Gaussian 模式：只做远处模糊，最适合当前 2.5D 地图调试。
        SetEnumParameter(dof, "mode", "Gaussian");
        SetFloatParameter(dof, "gaussianStart", Mathf.Max(0.1f, focusDistance));
        SetFloatParameter(dof, "gaussianEnd", Mathf.Max(focusDistance + 0.1f, focusDistance + farBlurRange));
        SetFloatParameter(dof, "gaussianMaxRadius", Mathf.Clamp01(blurStrength));
        SetBoolParameter(dof, "highQualitySampling", highQualitySampling);

        // 兼容部分 URP 版本的 Bokeh 字段；即使当前 mode 使用 Gaussian，也保持值合理。
        SetFloatParameter(dof, "focusDistance", Mathf.Max(0.1f, focusDistance));
        SetFloatParameter(dof, "aperture", Mathf.Lerp(8f, 2.2f, Mathf.Clamp01(blurStrength)));
        SetFloatParameter(dof, "focalLength", Mathf.Lerp(35f, 85f, Mathf.Clamp01(blurStrength)));
    }

    private static Type FindUniversalDepthOfFieldType()
    {
        Type type = Type.GetType("UnityEngine.Rendering.Universal.DepthOfField, Unity.RenderPipelines.Universal.Runtime");
        if (type != null)
            return type;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            type = assemblies[i].GetType("UnityEngine.Rendering.Universal.DepthOfField");
            if (type != null)
                return type;
        }

        return null;
    }

    private static VolumeComponent GetOrAddVolumeComponent(VolumeProfile profile, Type componentType)
    {
        if (profile == null || componentType == null)
            return null;

        for (int i = 0; i < profile.components.Count; i++)
        {
            VolumeComponent existing = profile.components[i];
            if (existing != null && existing.GetType() == componentType)
                return existing;
        }

        MethodInfo addMethod = typeof(VolumeProfile).GetMethod("Add", new[] { typeof(Type), typeof(bool) });
        if (addMethod != null)
        {
            VolumeComponent added = addMethod.Invoke(profile, new object[] { componentType, true }) as VolumeComponent;
            if (added != null)
                return added;
        }

        VolumeComponent created = ScriptableObject.CreateInstance(componentType) as VolumeComponent;
        if (created != null)
        {
            created.name = componentType.Name;
            created.active = true;
            profile.components.Add(created);
            return created;
        }

        return null;
    }

    private static void SetFloatParameter(VolumeComponent component, string fieldName, float value)
    {
        object parameter = GetFieldValue(component, fieldName);
        if (parameter == null)
            return;

        SetParameterValue(parameter, value);
    }

    private static void SetBoolParameter(VolumeComponent component, string fieldName, bool value)
    {
        object parameter = GetFieldValue(component, fieldName);
        if (parameter == null)
            return;

        SetParameterValue(parameter, value);
    }

    private static void SetEnumParameter(VolumeComponent component, string fieldName, string enumName)
    {
        object parameter = GetFieldValue(component, fieldName);
        if (parameter == null)
            return;

        Type parameterType = parameter.GetType();
        FieldInfo valueField = parameterType.GetField("value");
        if (valueField == null || !valueField.FieldType.IsEnum)
            return;

        try
        {
            object enumValue = Enum.Parse(valueField.FieldType, enumName);
            SetParameterValue(parameter, enumValue);
        }
        catch
        {
            // 不同 URP 版本若枚举名不同，静默跳过，避免影响编译和运行。
        }
    }

    private static object GetFieldValue(VolumeComponent component, string fieldName)
    {
        if (component == null || string.IsNullOrEmpty(fieldName))
            return null;

        FieldInfo field = component.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null ? field.GetValue(component) : null;
    }

    private static void SetParameterValue(object parameter, object value)
    {
        Type parameterType = parameter.GetType();

        FieldInfo overrideField = parameterType.GetField("overrideState");
        if (overrideField != null)
            overrideField.SetValue(parameter, true);

        FieldInfo valueField = parameterType.GetField("value");
        if (valueField != null)
            valueField.SetValue(parameter, ConvertValue(value, valueField.FieldType));
    }

    private static object ConvertValue(object value, Type targetType)
    {
        if (targetType == typeof(float))
            return Convert.ToSingle(value);
        if (targetType == typeof(bool))
            return Convert.ToBoolean(value);
        if (targetType.IsEnum && value is string text)
            return Enum.Parse(targetType, text);
        return value;
    }
}
