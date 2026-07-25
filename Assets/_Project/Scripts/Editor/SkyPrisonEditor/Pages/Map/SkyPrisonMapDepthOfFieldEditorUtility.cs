using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 地图景深结构同步工具。
/// 规则：不改动现有摄像机栈语义；景深只绑定到 Base 世界相机，Overlay 相机只叠角色/UI/迷雾。
/// </summary>
public static class SkyPrisonMapDepthOfFieldEditorUtility
{
    private const string CameraSystemName = "CameraSystem";
    private const string RenderSystemName = "RenderSystem";
    private const string MainCameraName = "Main Camera";
    private const string LegacyGamePlayCameraName = "GamePlayCamera";
    private const string VolumeName = "CameraPostProcessVolume";

    public static void ApplyDepthOfFieldToCurrentScene(MapDefinition map)
    {
        if (!TryGetValidMapAndScene(map, EditorSceneManager.GetActiveScene(), out Scene scene))
            return;

        ApplyDepthOfFieldToScene(map, scene, true);
    }

    public static void ApplyDepthOfFieldToMapScene(MapDefinition map)
    {
        if (map == null)
        {
            EditorUtility.DisplayDialog("镜头景深", "请先选择地图定义。", "确定");
            return;
        }

        string scenePath = SkyPrisonMapEditorUtility.ResolveMapScenePath(map, true);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            EditorUtility.DisplayDialog("镜头景深", "当前地图没有绑定 Scene。", "确定");
            return;
        }

        Scene active = EditorSceneManager.GetActiveScene();
        bool alreadyOpen = active.IsValid() && active.path == scenePath;
        Scene targetScene = alreadyOpen ? active : EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        ApplyDepthOfFieldToScene(map, targetScene, true);
        EditorSceneManager.SaveScene(targetScene);
    }

    public static void InspectDepthOfFieldStructureCurrentScene(MapDefinition map)
    {
        if (!TryGetValidMapAndScene(map, EditorSceneManager.GetActiveScene(), out Scene scene))
            return;

        EditorUtility.DisplayDialog("景深结构检查", BuildDepthOfFieldStructureReport(map, scene), "确定");
    }

    public static void AutoFixDepthOfFieldStructureCurrentScene(MapDefinition map)
    {
        if (!TryGetValidMapAndScene(map, EditorSceneManager.GetActiveScene(), out Scene scene))
            return;

        ApplyDepthOfFieldToScene(map, scene, false);
        EditorUtility.DisplayDialog("景深结构自动补齐 / 矫正", BuildDepthOfFieldStructureReport(map, scene), "确定");
    }

    public static void ApplyDepthOfFieldToScene(MapDefinition map, Scene scene, bool showDialogWhenDone)
    {
        if (map == null || !scene.IsValid())
            return;

        GameObject renderSystem = FindOrCreateRootOrChild(scene, RenderSystemName, "System");
        GameObject cameraSystem = FindOrCreateRootOrChild(scene, CameraSystemName, "System");

        Camera baseCamera = FindOrCreateWorldBaseCamera(scene, cameraSystem.transform);
        Volume volume = FindOrCreateCameraPostProcessVolume(scene, renderSystem.transform);

        if (baseCamera == null || volume == null)
        {
            EditorUtility.DisplayDialog("镜头景深", "景深结构创建失败：没有可用的 Base 世界相机或 Volume。", "确定");
            return;
        }

        Undo.RecordObject(baseCamera.gameObject, "Apply Camera Depth Of Field");
        Undo.RecordObject(volume.gameObject, "Apply Camera Depth Of Field");

        ConfigureBaseCameraForPostProcessing(baseCamera, volume);

        // 这里不要给相机挂运行时 Controller。
        // 原因：如果用户误把 Controller 文件放在 Editor 文件夹，Undo.AddComponent 会失败；
        // 而景深结构本身只需要把参数写入 Volume Profile，就能在当前地图 Scene 生效。
        // 运行时动态调参以后再由 Core/Camera 下的纯运行时 Controller 承担。
        ApplyDepthOfFieldDirectToVolumeProfile(map, volume);

        EditorUtility.SetDirty(baseCamera);
        EditorUtility.SetDirty(baseCamera.gameObject);
        EditorUtility.SetDirty(volume);
        EditorUtility.SetDirty(volume.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);

        if (showDialogWhenDone)
        {
            string state = map.enableDepthOfField ? "已开启" : "已关闭";
            EditorUtility.DisplayDialog(
                "镜头景深",
                $"景深同步完成。\n\n目标相机：{baseCamera.name}\n状态：{state}\n焦点距离：{map.focusDistance:0.##}\n模糊强度：{map.blurStrength:0.##}\n\n提示：景深作用在 Base 世界相机上，Overlay 角色相机不会作为景深主目标。",
                "确定");
        }
    }

    public static string BuildDepthOfFieldStructureReport(MapDefinition map, Scene scene)
    {
        StringBuilder sb = new StringBuilder(1024);
        sb.AppendLine("天空囚笼 / 景深结构检查");
        sb.AppendLine();

        Camera baseCamera = FindWorldBaseCamera(scene);
        Camera gameplayCamera = FindCameraByNameInScene(scene, LegacyGamePlayCameraName);
        Volume volume = FindVolumeByNameInScene(scene, VolumeName);

        if (baseCamera == null)
        {
            sb.AppendLine("✗ Base 世界相机：未找到");
            sb.AppendLine("  建议：点击“自动补齐/矫正景深结构”。");
        }
        else
        {
            sb.AppendLine($"✓ Base 世界相机：{baseCamera.name}");
            sb.AppendLine($"  Render Type：{GetUniversalCameraRenderTypeName(baseCamera)}");
            sb.AppendLine($"  Culling Mask：{LayerMaskToNames(baseCamera.cullingMask)}");
            sb.AppendLine($"  Post Processing：{(GetRenderPostProcessing(baseCamera) ? "开启" : "关闭")}");
            sb.AppendLine($"  Depth Texture：{GetDepthTextureStateName(baseCamera)}");
        }

        sb.AppendLine();

        if (gameplayCamera != null)
        {
            sb.AppendLine($"Overlay 角色相机：{gameplayCamera.name}");
            sb.AppendLine($"  Render Type：{GetUniversalCameraRenderTypeName(gameplayCamera)}");
            sb.AppendLine($"  Culling Mask：{LayerMaskToNames(gameplayCamera.cullingMask)}");
            sb.AppendLine("  说明：它应负责角色/UI/迷雾叠加，不作为地图景深主目标。");
        }
        else
        {
            sb.AppendLine("Overlay 角色相机：未找到 GamePlayCamera（不一定是问题）。");
        }

        sb.AppendLine();

        if (volume == null)
        {
            sb.AppendLine("✗ CameraPostProcessVolume：未找到");
        }
        else
        {
            sb.AppendLine($"✓ CameraPostProcessVolume：{volume.name}");
            sb.AppendLine($"  Is Global：{volume.isGlobal}");
            sb.AppendLine($"  Priority：{volume.priority:0.##}");
            sb.AppendLine($"  Profile：{(volume.profile != null ? "存在" : "缺失")}");
            sb.AppendLine($"  Depth Of Field：{(HasUniversalDepthOfField(volume.profile, out bool active) ? (active ? "存在 / Active" : "存在 / Inactive") : "缺失")}");
        }

        sb.AppendLine();
        sb.AppendLine("自动补齐/矫正会做：");
        sb.AppendLine("1. 优先选择 Render Type = Base 的 Main Camera / 世界相机。 ");
        sb.AppendLine("2. 给 Base 相机开启 Post Processing 与 Depth Texture。 ");
        sb.AppendLine("3. 创建或修复 CameraPostProcessVolume。 ");
        sb.AppendLine("4. 直接把景深参数写入 CameraPostProcessVolume 的 Volume Profile。 ");
        sb.AppendLine("5. 不强制给相机挂运行时 Controller，避免 Editor/Core 依赖反向污染。 ");
        sb.AppendLine("6. 不改 GamePlayCamera 的 Overlay 职责，不破坏原相机栈。 ");

        if (baseCamera != null && baseCamera.orthographic)
        {
            sb.AppendLine();
            sb.AppendLine("注意：当前 Base 相机是 Orthographic。URP 原生 DOF 在正交视角下可能不如透视镜头明显，测试时请先把模糊强度拉高。后续如果需要更稳定的 2.5D 远景虚化，可以单独做屏幕空间/高度虚化。 ");
        }

        return sb.ToString();
    }

    private static bool TryGetValidMapAndScene(MapDefinition map, Scene scene, out Scene validScene)
    {
        validScene = scene;
        if (map == null)
        {
            EditorUtility.DisplayDialog("镜头景深", "请先选择地图定义。", "确定");
            return false;
        }
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("镜头景深", "当前 Scene 无效。", "确定");
            return false;
        }
        return true;
    }

    private static Camera FindOrCreateWorldBaseCamera(Scene scene, Transform cameraSystem)
    {
        Camera camera = FindWorldBaseCamera(scene);
        if (camera != null)
            return camera;

        GameObject obj = new GameObject(MainCameraName);
        if (scene.IsValid())
            EditorSceneManager.MoveGameObjectToScene(obj, scene);
        if (cameraSystem != null)
            obj.transform.SetParent(cameraSystem, false);

        obj.transform.position = new Vector3(0f, 40f, 12f);
        obj.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
        obj.tag = "MainCamera";

        camera = obj.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 8f;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 1000f;
        camera.cullingMask = ~0;

        SetUniversalCameraRenderType(camera, "Base");
        return camera;
    }

    private static Camera FindWorldBaseCamera(Scene scene)
    {
        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        Camera mainNamedBase = null;
        Camera taggedMainBase = null;
        Camera anyBaseWorld = null;
        Camera anyNonOverlay = null;

        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera == null || camera.gameObject.scene != scene)
                continue;

            string renderType = GetUniversalCameraRenderTypeName(camera);
            bool isOverlay = string.Equals(renderType, "Overlay", StringComparison.OrdinalIgnoreCase);
            bool isBase = string.Equals(renderType, "Base", StringComparison.OrdinalIgnoreCase) || string.Equals(renderType, "Unknown", StringComparison.OrdinalIgnoreCase);
            bool looksWorld = LooksLikeWorldCamera(camera);

            if (isBase && camera.gameObject.name == MainCameraName)
                mainNamedBase = camera;
            if (isBase && camera.CompareTag("MainCamera"))
                taggedMainBase = camera;
            if (isBase && looksWorld && anyBaseWorld == null)
                anyBaseWorld = camera;
            if (!isOverlay && anyNonOverlay == null)
                anyNonOverlay = camera;
        }

        if (mainNamedBase != null) return mainNamedBase;
        if (taggedMainBase != null) return taggedMainBase;
        if (anyBaseWorld != null) return anyBaseWorld;
        return anyNonOverlay;
    }

    private static bool LooksLikeWorldCamera(Camera camera)
    {
        if (camera == null)
            return false;

        string name = camera.gameObject.name.ToLowerInvariant();
        if (name.Contains("gameplay") || name.Contains("ui") || name.Contains("outline") || name.Contains("overhead"))
            return false;

        string mask = LayerMaskToNames(camera.cullingMask).ToLowerInvariant();
        if (mask.Contains("world") || mask.Contains("ground") || mask.Contains("terrain") || mask.Contains("default") || mask.Contains("map"))
            return true;

        return camera.cullingMask == ~0;
    }

    private static Volume FindOrCreateCameraPostProcessVolume(Scene scene, Transform renderSystem)
    {
        Volume volume = FindVolumeByNameInScene(scene, VolumeName);
        GameObject obj = volume != null ? volume.gameObject : null;

        if (obj == null)
        {
            obj = new GameObject(VolumeName);
            if (scene.IsValid())
                EditorSceneManager.MoveGameObjectToScene(obj, scene);
        }

        if (renderSystem != null && obj.transform.parent == null)
            obj.transform.SetParent(renderSystem, false);

        volume = obj.GetComponent<Volume>();
        if (volume == null)
            volume = Undo.AddComponent<Volume>(obj);

        volume.isGlobal = true;
        volume.priority = 10f;
        if (volume.profile == null)
        {
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CameraPostProcessVolumeProfile_RuntimeGenerated";
            volume.profile = profile;
        }

        return volume;
    }

    private static void ConfigureBaseCameraForPostProcessing(Camera camera, Volume volume)
    {
        if (camera == null)
            return;

        camera.depthTextureMode |= DepthTextureMode.Depth;
        SetUniversalCameraRenderType(camera, "Base");

        Type additionalCameraDataType = FindType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
        if (additionalCameraDataType == null)
            return;

        Component additionalData = camera.GetComponent(additionalCameraDataType);
        if (additionalData == null)
            additionalData = Undo.AddComponent(camera.gameObject, additionalCameraDataType);

        SetPropertyOrField(additionalData, "renderPostProcessing", true);
        SetPropertyOrField(additionalData, "requiresDepthTextureOption", EnumValue("UnityEngine.Rendering.Universal.CameraOverrideOption", "On"));
        SetPropertyOrField(additionalData, "requiresColorTextureOption", EnumValue("UnityEngine.Rendering.Universal.CameraOverrideOption", "On"));

        if (volume != null)
        {
            SetPropertyOrField(additionalData, "volumeTrigger", camera.transform);
            SetPropertyOrField(additionalData, "volumeLayerMask", ~0);
        }

        EditorUtility.SetDirty(additionalData);
    }

    private static void CleanupLegacyOverlayDepthOfFieldControllers(Scene scene)
    {
        // 保留空方法占位：旧版本曾尝试把景深 Controller 挂到 Overlay 相机。
        // 新版本不再依赖运行时 Controller，因此这里不做任何强制删除，避免误删用户组件。
    }

    private static void ApplyDepthOfFieldDirectToVolumeProfile(MapDefinition map, Volume volume)
    {
        if (map == null || volume == null)
            return;

        volume.isGlobal = true;
        volume.priority = 10f;

        if (volume.profile == null)
        {
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "CameraPostProcessVolumeProfile_RuntimeGenerated";
            volume.profile = profile;
        }

        VolumeProfile targetProfile = volume.profile;
        Type dofType = FindType("UnityEngine.Rendering.Universal.DepthOfField");
        if (dofType == null)
        {
            Debug.LogWarning("[SkyPrisonMapDepthOfFieldEditorUtility] 未找到 URP DepthOfField 类型。请确认项目正在使用 Universal Render Pipeline。", volume);
            return;
        }

        VolumeComponent dof = GetOrAddVolumeComponent(targetProfile, dofType);
        if (dof == null)
        {
            Debug.LogWarning("[SkyPrisonMapDepthOfFieldEditorUtility] 无法创建 Depth Of Field Volume Override。", volume);
            return;
        }

        Undo.RecordObject(targetProfile, "Apply Depth Of Field Volume Profile");
        Undo.RecordObject(dof, "Apply Depth Of Field Volume Override");

        float focus = Mathf.Max(0.1f, map.focusDistance);
        float strength = Mathf.Clamp01(map.blurStrength);
        float range = Mathf.Lerp(8f, 36f, 1f - Mathf.Clamp01(strength));
        bool enabled = map.enableDepthOfField && strength > 0.001f && !SkyPrisonRenderQualityContext.IsSafe;

        dof.active = enabled;

        // URP Gaussian 模式：对 2.5D 正交地图比 Bokeh 更直接。
        SetVolumeEnumParameter(dof, "mode", "Gaussian");
        SetVolumeFloatParameter(dof, "gaussianStart", focus);
        SetVolumeFloatParameter(dof, "gaussianEnd", Mathf.Max(focus + 0.1f, focus + range));
        SetVolumeFloatParameter(dof, "gaussianMaxRadius", strength);
        SetVolumeBoolParameter(dof, "highQualitySampling", SkyPrisonRenderQualityContext.IsFinal);

        // 兼容 Bokeh 字段，避免不同 URP 版本切换模式时参数为空。
        SetVolumeFloatParameter(dof, "focusDistance", focus);
        SetVolumeFloatParameter(dof, "aperture", Mathf.Lerp(8f, 2.2f, strength));
        SetVolumeFloatParameter(dof, "focalLength", Mathf.Lerp(35f, 85f, strength));

        EditorUtility.SetDirty(dof);
        EditorUtility.SetDirty(targetProfile);
        EditorUtility.SetDirty(volume);
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
            try
            {
                VolumeComponent added = addMethod.Invoke(profile, new object[] { componentType, true }) as VolumeComponent;
                if (added != null)
                    return added;
            }
            catch { }
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

    private static void SetVolumeFloatParameter(VolumeComponent component, string fieldName, float value)
    {
        object parameter = GetPropertyOrField(component, fieldName);
        SetVolumeParameterValue(parameter, value);
    }

    private static void SetVolumeBoolParameter(VolumeComponent component, string fieldName, bool value)
    {
        object parameter = GetPropertyOrField(component, fieldName);
        SetVolumeParameterValue(parameter, value);
    }

    private static void SetVolumeEnumParameter(VolumeComponent component, string fieldName, string enumName)
    {
        object parameter = GetPropertyOrField(component, fieldName);
        if (parameter == null)
            return;

        Type parameterType = parameter.GetType();
        FieldInfo valueField = parameterType.GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (valueField == null || !valueField.FieldType.IsEnum)
            return;

        try
        {
            object enumValue = Enum.Parse(valueField.FieldType, enumName);
            SetVolumeParameterValue(parameter, enumValue);
        }
        catch { }
    }

    private static void SetVolumeParameterValue(object parameter, object value)
    {
        if (parameter == null || value == null)
            return;

        Type parameterType = parameter.GetType();
        FieldInfo overrideField = parameterType.GetField("overrideState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (overrideField != null)
            overrideField.SetValue(parameter, true);

        FieldInfo valueField = parameterType.GetField("value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (valueField != null)
            valueField.SetValue(parameter, ConvertValue(value, valueField.FieldType));
    }

    private static GameObject FindOrCreateRootOrChild(Scene scene, string name, string preferredParentName)
    {
        GameObject found = FindGameObjectByNameInScene(scene, name);
        if (found != null)
            return found;

        GameObject parent = FindGameObjectByNameInScene(scene, preferredParentName);
        GameObject obj = new GameObject(name);
        if (scene.IsValid())
            EditorSceneManager.MoveGameObjectToScene(obj, scene);
        if (parent != null)
            obj.transform.SetParent(parent.transform, false);
        return obj;
    }

    private static GameObject FindGameObjectByNameInScene(Scene scene, string name)
    {
        if (!scene.IsValid() || string.IsNullOrEmpty(name))
            return null;

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, name);
            if (found != null)
                return found.gameObject;
        }
        return null;
    }

    private static Transform FindChildRecursive(Transform root, string name)
    {
        if (root == null)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Camera FindCameraByNameInScene(Scene scene, string name)
    {
        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera camera = cameras[i];
            if (camera != null && camera.gameObject.scene == scene && camera.gameObject.name == name)
                return camera;
        }
        return null;
    }

    private static Volume FindVolumeByNameInScene(Scene scene, string name)
    {
        Volume[] volumes = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (volume != null && volume.gameObject.scene == scene && volume.gameObject.name == name)
                return volume;
        }
        return null;
    }

    private static string GetUniversalCameraRenderTypeName(Camera camera)
    {
        object data = GetAdditionalCameraData(camera, false);
        if (data == null)
            return "Unknown";

        object value = GetPropertyOrField(data, "renderType");
        return value != null ? value.ToString() : "Unknown";
    }

    private static void SetUniversalCameraRenderType(Camera camera, string renderTypeName)
    {
        object data = GetAdditionalCameraData(camera, true);
        if (data == null)
            return;

        Type dataType = data.GetType();
        PropertyInfo property = dataType.GetProperty("renderType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        FieldInfo field = dataType.GetField("renderType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Type valueType = property != null ? property.PropertyType : field != null ? field.FieldType : null;
        if (valueType == null || !valueType.IsEnum)
            return;

        try
        {
            object enumValue = Enum.Parse(valueType, renderTypeName);
            if (property != null && property.CanWrite)
                property.SetValue(data, enumValue);
            else if (field != null)
                field.SetValue(data, enumValue);
        }
        catch { }
    }

    private static bool GetRenderPostProcessing(Camera camera)
    {
        object data = GetAdditionalCameraData(camera, false);
        if (data == null)
            return false;
        object value = GetPropertyOrField(data, "renderPostProcessing");
        return value is bool b && b;
    }

    private static string GetDepthTextureStateName(Camera camera)
    {
        object data = GetAdditionalCameraData(camera, false);
        if (data == null)
            return camera.depthTextureMode.ToString();
        object value = GetPropertyOrField(data, "requiresDepthTextureOption");
        return value != null ? value.ToString() : camera.depthTextureMode.ToString();
    }

    private static object GetAdditionalCameraData(Camera camera, bool create)
    {
        if (camera == null)
            return null;

        Type additionalCameraDataType = FindType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
        if (additionalCameraDataType == null)
            return null;

        Component additionalData = camera.GetComponent(additionalCameraDataType);
        if (additionalData == null && create)
            additionalData = Undo.AddComponent(camera.gameObject, additionalCameraDataType);
        return additionalData;
    }

    private static bool HasUniversalDepthOfField(VolumeProfile profile, out bool active)
    {
        active = false;
        if (profile == null)
            return false;

        Type dofType = FindType("UnityEngine.Rendering.Universal.DepthOfField");
        if (dofType == null)
            return false;

        for (int i = 0; i < profile.components.Count; i++)
        {
            VolumeComponent component = profile.components[i];
            if (component != null && component.GetType() == dofType)
            {
                active = component.active;
                return true;
            }
        }
        return false;
    }

    private static string LayerMaskToNames(int mask)
    {
        if (mask == ~0)
            return "Everything";
        if (mask == 0)
            return "Nothing";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1 << i)) == 0)
                continue;
            string layerName = LayerMask.LayerToName(i);
            if (string.IsNullOrEmpty(layerName))
                continue;
            if (sb.Length > 0)
                sb.Append(", ");
            sb.Append(layerName);
        }
        return sb.Length > 0 ? sb.ToString() : mask.ToString();
    }

    private static Type FindType(string fullName)
    {
        Type type = Type.GetType(fullName + ", Unity.RenderPipelines.Universal.Runtime");
        if (type != null)
            return type;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            type = assemblies[i].GetType(fullName);
            if (type != null)
                return type;
        }
        return null;
    }

    private static object EnumValue(string enumFullName, string valueName)
    {
        Type type = FindType(enumFullName);
        if (type == null || !type.IsEnum)
            return null;

        try { return Enum.Parse(type, valueName); }
        catch { return null; }
    }

    private static object GetPropertyOrField(object target, string name)
    {
        if (target == null || string.IsNullOrEmpty(name))
            return null;

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanRead)
        {
            try { return property.GetValue(target); }
            catch { }
        }

        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            try { return field.GetValue(target); }
            catch { }
        }

        return null;
    }

    private static void SetPropertyOrField(object target, string name, object value)
    {
        if (target == null || string.IsNullOrEmpty(name) || value == null)
            return;

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanWrite)
        {
            try
            {
                property.SetValue(target, ConvertValue(value, property.PropertyType));
                return;
            }
            catch { }
        }

        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
        {
            try { field.SetValue(target, ConvertValue(value, field.FieldType)); }
            catch { }
        }
    }

    private static object ConvertValue(object value, Type targetType)
    {
        if (value == null)
            return null;
        if (targetType.IsInstanceOfType(value))
            return value;
        if (targetType == typeof(LayerMask) && value is int maskValue)
            return (LayerMask)maskValue;
        if (targetType.IsEnum && value.GetType().IsEnum)
            return Enum.ToObject(targetType, Convert.ToInt32(value));
        return value;
    }
}
