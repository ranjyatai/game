using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class SkyPrisonMapEnvironmentEditorUtility
{
    private const string EnvironmentRootName = "__SkyPrisonMapEnvironment";
    private const string EnvironmentAreaLightName = "Environment_AreaLight";
    private const string MainLightName = "Main_DirectionalLight";
    private const string SkyModelRootName = "SkyRenderModel";
    private const string EffectRootName = "EnvironmentEffect";
    private const string PostProcessVolumeName = "CameraPostProcessVolume";
    private const string GrassColorMapRendererName = "GrassColorMapRenderer";

    public static void InspectEnvironmentStructureCurrentScene(MapDefinition map)
    {
        if (map == null)
        {
            EditorUtility.DisplayDialog("地图环境", "未选择地图定义。", "确定");
            return;
        }

        GameObject root = FindInActiveScene(EnvironmentRootName);
        Light area = FindEnvironmentAreaLight();
        Light main = FindMainDirectionalLight();
        Volume volume = FindPostProcessVolume();
        Component grassColorMap = FindGrassColorMapRenderer();

        Debug.Log(
            "[SkyPrisonMapEnvironment] 当前 Scene 环境结构：\n" +
            $"Root: {(root != null ? root.name : "缺失")}\n" +
            $"Environment Area Light: {(area != null ? area.name : "缺失")}\n" +
            $"Environment Area Light Type: {(area != null ? area.type.ToString() : "-")}\n" +
            $"Main Directional Light: {(main != null ? main.name : "缺失")}\n" +
            $"Skybox: {(RenderSettings.skybox != null ? RenderSettings.skybox.name : "None")}\n" +
            $"Ambient Mode: {RenderSettings.ambientMode}\n" +
            $"Fog: {RenderSettings.fog}\n" +
            $"PostProcess Volume: {(volume != null ? volume.name : "缺失")}\n" +
            $"Grass Color Map Renderer: {(grassColorMap != null ? grassColorMap.name + " / " + grassColorMap.GetType().FullName : "缺失")}\n\n" +
            "规则：地图环境补光默认使用 Ambient + Area Light；Point Light 不再作为环境光。草地融合节点默认挂在环境根节点下，只补齐节点和组件，不改草材质。");
    }

    public static void AutoFixEnvironmentStructureCurrentScene(MapDefinition map)
    {
        if (map == null)
            return;

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName("Auto Fix Map Environment Structure");

        Scene scene = SceneManager.GetActiveScene();
        GameObject root = EnsureEnvironmentRoot(scene);
        EnsureEnvironmentAreaLight(root.transform);
        EnsureMainDirectionalLight(root.transform);
        EnsureSkyRenderModel(root.transform, map);
        EnsureEnvironmentEffect(root.transform, map);
        EnsurePostProcessVolume(root.transform, map);
        EnsureGrassColorMapRenderer(root.transform, map, scene);

        RemoveWrongEnvironmentPointLight(root.transform);

        EditorSceneManager.MarkSceneDirty(scene);
        SceneView.RepaintAll();
        Debug.Log("[SkyPrisonMapEnvironment] 已自动补齐/矫正当前 Scene 的地图环境结构。默认环境光为 Area Light，不再生成 Point Light。", root);
    }

    public static void ApplyEnvironmentToCurrentScene(MapDefinition map)
    {
        ApplyEnvironmentToScene(map, SceneManager.GetActiveScene(), false);
    }

    public static void ApplyEnvironmentToScene(MapDefinition map, Scene scene, bool saveScene)
    {
        if (map == null)
            return;

        if (!scene.IsValid())
            scene = SceneManager.GetActiveScene();

        GameObject root = EnsureEnvironmentRoot(scene);
        Light area = EnsureEnvironmentAreaLight(root.transform);
        Light main = EnsureMainDirectionalLight(root.transform);
        EnsureSkyRenderModel(root.transform, map);
        EnsureEnvironmentEffect(root.transform, map);
        Volume volume = EnsurePostProcessVolume(root.transform, map);
        EnsureGrassColorMapRenderer(root.transform, map, scene);

        RemoveWrongEnvironmentPointLight(root.transform);

        RenderSettings.skybox = map.skyboxMaterial;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = map.ambientColor;
        RenderSettings.fog = map.enableSceneFog;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = map.sceneFogColor;
        RenderSettings.fogStartDistance = map.fogStartDistance;
        RenderSettings.fogEndDistance = Mathf.Max(map.fogStartDistance + 0.01f, map.fogEndDistance);

        if (area != null)
        {
            area.type = LightType.Rectangle;
            area.color = map.environmentAreaLightColor;
            area.intensity = map.environmentAreaLightIntensity;
            area.shadows = LightShadows.None;
            area.range = Mathf.Max(1f, map.environmentAreaLightSize);
            area.areaSize = Vector2.one * Mathf.Max(0.1f, map.environmentAreaLightSize);
            area.transform.position = map.environmentAreaLightPosition;
            area.transform.rotation = Quaternion.Euler(map.environmentAreaLightEuler);
            EditorUtility.SetDirty(area);
        }

        if (main != null)
        {
            main.type = LightType.Directional;
            main.color = map.mainLightColor;
            main.intensity = map.mainLightIntensity;
            main.shadows = LightShadows.Soft;
            main.shadowStrength = 0.45f;
            main.transform.rotation = Quaternion.Euler(map.mainLightEuler);
            EditorUtility.SetDirty(main);
        }

        if (volume != null)
            EditorUtility.SetDirty(volume);

        EditorSceneManager.MarkSceneDirty(scene);
        if (saveScene)
            EditorSceneManager.SaveScene(scene);

        SceneView.RepaintAll();
        Debug.Log("[SkyPrisonMapEnvironment] 已同步地图环境到 Scene。默认环境补光使用 Ambient + Area Light。", root);
    }

    public static void PullEnvironmentFromCurrentScene(MapDefinition map)
    {
        if (map == null)
            return;

        Undo.RecordObject(map, "Pull Map Environment From Scene");

        Light area = FindEnvironmentAreaLight();
        Light main = FindMainDirectionalLight();
        Volume volume = FindPostProcessVolume();

        map.skyboxMaterial = RenderSettings.skybox;
        map.ambientColor = RenderSettings.ambientLight;
        map.enableSceneFog = RenderSettings.fog;
        map.sceneFogColor = RenderSettings.fogColor;
        map.fogStartDistance = RenderSettings.fogStartDistance;
        map.fogEndDistance = RenderSettings.fogEndDistance;

        if (area != null)
        {
            map.environmentAreaLightColor = area.color;
            map.environmentAreaLightIntensity = area.intensity;
            map.environmentAreaLightSize = Mathf.Max(0.1f, Mathf.Max(area.areaSize.x, area.areaSize.y));
            map.environmentAreaLightPosition = area.transform.position;
            map.environmentAreaLightEuler = area.transform.rotation.eulerAngles;
        }

        if (main != null)
        {
            map.mainLightColor = main.color;
            map.mainLightIntensity = main.intensity;
            map.mainLightEuler = main.transform.rotation.eulerAngles;
        }

        if (volume != null)
            map.postProcessProfile = volume.profile;

        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        Debug.Log("[SkyPrisonMapEnvironment] 已从当前 Scene 回读环境数据到 MapDefinition，页面刷新后会显示这些值。", map);
    }

    public static void ApplyEnvironmentToMapScene(MapDefinition map)
    {
        if (map == null)
            return;

        string scenePath = SkyPrisonMapEditorUtility.ResolveMapScenePath(map, true);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            EditorUtility.DisplayDialog("地图环境", "找不到当前地图绑定的 Scene。", "确定");
            return;
        }

        Scene active = SceneManager.GetActiveScene();
        string activePath = active.path.Replace("\\", "/");
        if (activePath == scenePath.Replace("\\", "/"))
        {
            ApplyEnvironmentToScene(map, active, true);
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Scene opened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        ApplyEnvironmentToScene(map, opened, true);
    }

    public static void EnsureGrassColorMapRendererCurrentScene(MapDefinition map)
    {
        EnsureAndRenderGrassColorMapCurrentScene(map, false);
    }

    public static void EnsureAndRenderGrassColorMapCurrentScene(MapDefinition map, bool showDialogWhenDone = true)
    {
        if (map == null)
            return;

        Scene scene = SceneManager.GetActiveScene();
        GameObject root = EnsureEnvironmentRoot(scene);
        Component component = EnsureGrassColorMapRenderer(root.transform, map, scene);

        bool assignedTerrain = false;
        GameObject groundTerrainObject = FindGroundTerrainObjectInScene(scene);
        Terrain terrain = groundTerrainObject != null ? groundTerrainObject.GetComponent<Terrain>() : FindGroundTerrainInScene(scene);
        if (groundTerrainObject == null && terrain != null)
            groundTerrainObject = terrain.gameObject;

        if (component != null && groundTerrainObject != null)
            assignedTerrain = TryAssignTerrainToColorMapRenderer(component, terrain, groundTerrainObject);

        bool adjustedArea = component != null && TryAdjustGrassColorMapRenderArea(component, map, terrain);
        bool rendered = component != null && TryRenderGrassColorMap(component);

        EditorSceneManager.MarkSceneDirty(scene);
        SceneView.RepaintAll();

        if (component == null)
        {
            Debug.LogWarning("[SkyPrisonMapEnvironment] 已创建草地 Color Map 节点，但没有找到 Stylized Grass 的 ColorMapRenderer 组件类型。请确认 Stylized Grass Shader 插件已导入，或手动给节点添加 Stylized Grass / Color Map Renderer 组件。");
            return;
        }

        string message = string.Join("\n", new[]
        {
            "草地 Color Map 节点已处理。",
            $"组件：{component.GetType().FullName}",
            $"Terrain：{(groundTerrainObject != null ? groundTerrainObject.name : "未找到 GroundTerrain / Terrain")}",
            $"自动写入 Terrain：{(assignedTerrain ? "成功" : "未执行 / 未找到可写字段")}",
            $"自动对齐 Render Area：{(adjustedArea ? "已尝试" : "未找到可写字段或方法")}",
            $"自动 Render / Bake：{(rendered ? "已调用" : "未找到无参数 Render/Bake 方法，请在组件上手动点 Render")}"
        });

        if (showDialogWhenDone)
            EditorUtility.DisplayDialog("草地 Color Map", message, "确定");
        else
            Debug.Log("[SkyPrisonMapEnvironment] " + message, component);
    }

    public static void ApplyPresetDefaultValues(MapDefinition map)
    {
        if (map == null)
            return;

        Undo.RecordObject(map, "Apply Map Environment Preset");

        switch (map.environmentPreset)
        {
            case MapEnvironmentPreset.Day:
                map.ambientColor = new Color(0.72f, 0.70f, 0.67f, 1f);
                map.environmentAreaLightColor = new Color(0.72f, 0.70f, 0.67f, 1f);
                map.environmentAreaLightIntensity = 0.45f;
                map.environmentAreaLightSize = 16f;
                map.mainLightColor = Color.white;
                map.mainLightIntensity = 0.6f;
                map.sceneFogColor = new Color(0.58f, 0.64f, 0.68f, 1f);
                break;

            case MapEnvironmentPreset.Morning:
                map.ambientColor = new Color(0.70f, 0.66f, 0.58f, 1f);
                map.environmentAreaLightColor = new Color(0.72f, 0.66f, 0.58f, 1f);
                map.environmentAreaLightIntensity = 0.42f;
                map.environmentAreaLightSize = 18f;
                map.mainLightColor = new Color(1f, 0.82f, 0.62f, 1f);
                map.mainLightIntensity = 0.5f;
                map.sceneFogColor = new Color(0.62f, 0.58f, 0.52f, 1f);
                break;

            case MapEnvironmentPreset.Dusk:
                map.ambientColor = new Color(0.56f, 0.50f, 0.60f, 1f);
                map.environmentAreaLightColor = new Color(0.58f, 0.50f, 0.62f, 1f);
                map.environmentAreaLightIntensity = 0.4f;
                map.environmentAreaLightSize = 18f;
                map.mainLightColor = new Color(1.0f, 0.72f, 0.46f, 1f);
                map.mainLightIntensity = 0.5f;
                map.sceneFogColor = new Color(0.52f, 0.45f, 0.58f, 1f);
                break;

            case MapEnvironmentPreset.Night:
                map.ambientColor = new Color(0.24f, 0.30f, 0.42f, 1f);
                map.environmentAreaLightColor = new Color(0.28f, 0.34f, 0.46f, 1f);
                map.environmentAreaLightIntensity = 0.32f;
                map.environmentAreaLightSize = 20f;
                map.mainLightColor = new Color(0.55f, 0.66f, 0.95f, 1f);
                map.mainLightIntensity = 0.18f;
                map.sceneFogColor = new Color(0.20f, 0.25f, 0.34f, 1f);
                break;

            case MapEnvironmentPreset.InteriorCold:
                map.ambientColor = new Color(0.46f, 0.56f, 0.62f, 1f);
                map.environmentAreaLightColor = new Color(0.52f, 0.62f, 0.68f, 1f);
                map.environmentAreaLightIntensity = 0.5f;
                map.environmentAreaLightSize = 12f;
                map.mainLightColor = new Color(0.70f, 0.82f, 0.95f, 1f);
                map.mainLightIntensity = 0.25f;
                map.sceneFogColor = new Color(0.42f, 0.50f, 0.56f, 1f);
                break;

            case MapEnvironmentPreset.Underground:
                map.ambientColor = new Color(0.34f, 0.40f, 0.42f, 1f);
                map.environmentAreaLightColor = new Color(0.38f, 0.46f, 0.48f, 1f);
                map.environmentAreaLightIntensity = 0.42f;
                map.environmentAreaLightSize = 10f;
                map.mainLightColor = new Color(0.50f, 0.62f, 0.70f, 1f);
                map.mainLightIntensity = 0.16f;
                map.sceneFogColor = new Color(0.30f, 0.36f, 0.38f, 1f);
                break;

            case MapEnvironmentPreset.AlertRed:
                map.ambientColor = new Color(0.42f, 0.32f, 0.34f, 1f);
                map.environmentAreaLightColor = new Color(0.52f, 0.34f, 0.34f, 1f);
                map.environmentAreaLightIntensity = 0.35f;
                map.environmentAreaLightSize = 12f;
                map.mainLightColor = new Color(0.95f, 0.55f, 0.48f, 1f);
                map.mainLightIntensity = 0.28f;
                map.sceneFogColor = new Color(0.44f, 0.32f, 0.34f, 1f);
                break;

            case MapEnvironmentPreset.PollutedFog:
                map.ambientColor = new Color(0.46f, 0.56f, 0.48f, 1f);
                map.environmentAreaLightColor = new Color(0.50f, 0.62f, 0.54f, 1f);
                map.environmentAreaLightIntensity = 0.45f;
                map.environmentAreaLightSize = 18f;
                map.mainLightColor = new Color(0.88f, 0.94f, 0.84f, 1f);
                map.mainLightIntensity = 0.36f;
                map.sceneFogColor = new Color(0.36f, 0.46f, 0.40f, 1f);
                break;
        }

        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
    }

    private static GameObject EnsureEnvironmentRoot(Scene scene)
    {
        GameObject root = FindInScene(scene, EnvironmentRootName);
        if (root == null)
        {
            root = new GameObject(EnvironmentRootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Map Environment Root");
            if (scene.IsValid())
                SceneManager.MoveGameObjectToScene(root, scene);
        }

        return root;
    }

    private static Light EnsureEnvironmentAreaLight(Transform root)
    {
        Light light = FindEnvironmentAreaLight();
        if (light == null)
        {
            GameObject go = new GameObject(EnvironmentAreaLightName);
            Undo.RegisterCreatedObjectUndo(go, "Create Environment Area Light");
            go.transform.SetParent(root, false);
            light = go.AddComponent<Light>();
        }
        else if (light.transform.parent != root)
        {
            Undo.SetTransformParent(light.transform, root, "Move Environment Area Light");
        }

        light.name = EnvironmentAreaLightName;
        light.type = LightType.Rectangle;
        light.shadows = LightShadows.None;
        return light;
    }

    private static Light EnsureMainDirectionalLight(Transform root)
    {
        Light light = FindMainDirectionalLight();
        if (light == null)
        {
            GameObject go = new GameObject(MainLightName);
            Undo.RegisterCreatedObjectUndo(go, "Create Main Directional Light");
            go.transform.SetParent(root, false);
            light = go.AddComponent<Light>();
        }
        else if (light.transform.parent != root)
        {
            Undo.SetTransformParent(light.transform, root, "Move Main Directional Light");
        }

        light.name = MainLightName;
        light.type = LightType.Directional;
        return light;
    }

    private static void EnsureSkyRenderModel(Transform root, MapDefinition map)
    {
        if (root == null)
            return;

        Transform old = root.Find(SkyModelRootName);
        if (map == null || map.skyRenderModel == null)
        {
            if (old != null)
                Undo.DestroyObjectImmediate(old.gameObject);
            return;
        }

        if (old != null)
            return;

        GameObject go = PrefabUtility.InstantiatePrefab(map.skyRenderModel) as GameObject;
        if (go == null)
            go = Object.Instantiate(map.skyRenderModel);

        go.name = SkyModelRootName;
        Undo.RegisterCreatedObjectUndo(go, "Create Sky Render Model");
        go.transform.SetParent(root, false);
    }

    private static void EnsureEnvironmentEffect(Transform root, MapDefinition map)
    {
        if (root == null)
            return;

        Transform old = root.Find(EffectRootName);
        if (map == null || map.environmentFxPrefab == null)
        {
            if (old != null)
                Undo.DestroyObjectImmediate(old.gameObject);
            return;
        }

        if (old != null)
            return;

        GameObject go = PrefabUtility.InstantiatePrefab(map.environmentFxPrefab) as GameObject;
        if (go == null)
            go = Object.Instantiate(map.environmentFxPrefab);

        go.name = EffectRootName;
        Undo.RegisterCreatedObjectUndo(go, "Create Environment Effect");
        go.transform.SetParent(root, false);
    }

    // 天气特效改走运行时自动生成（MapWeatherController + MapWeatherRegistry，跟地图
    // BGM 同一套模式），进场景自动按当前场景配置生成，不需要在编辑器这边烘焙进
    // Scene——地图作者只要在"天气"表单里配好开启/类型/强度，MapWeatherRegistryBuilder
    // 保存 MapDefinition 时自动同步注册表，其余全自动，不用点按钮。

    private static Volume EnsurePostProcessVolume(Transform root, MapDefinition map)
    {
        Volume volume = FindPostProcessVolume();
        if (volume == null)
        {
            GameObject go = new GameObject(PostProcessVolumeName);
            Undo.RegisterCreatedObjectUndo(go, "Create Camera Post Process Volume");
            go.transform.SetParent(root, false);
            volume = go.AddComponent<Volume>();
        }
        else if (volume.transform.parent != root)
        {
            Undo.SetTransformParent(volume.transform, root, "Move Camera Post Process Volume");
        }

        volume.name = PostProcessVolumeName;
        volume.isGlobal = true;
        volume.priority = 0f;

        if (map != null)
            volume.profile = map.postProcessProfile;

        return volume;
    }

    private static Component EnsureGrassColorMapRenderer(Transform root, MapDefinition map, Scene scene)
    {
        if (root == null)
            return null;

        Transform child = root.Find(GrassColorMapRendererName);
        GameObject go;
        if (child == null)
        {
            go = new GameObject(GrassColorMapRendererName);
            Undo.RegisterCreatedObjectUndo(go, "Create Grass Color Map Renderer");
            go.transform.SetParent(root, false);
        }
        else
        {
            go = child.gameObject;
        }

        go.name = GrassColorMapRendererName;
        if (map != null)
        {
            go.transform.position = map.mapBoundsCenter;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        System.Type rendererType = FindStylizedGrassColorMapRendererType();
        if (rendererType == null)
            return null;

        Component component = go.GetComponent(rendererType);
        if (component == null)
        {
            component = Undo.AddComponent(go, rendererType);
        }

        TryAssignFirstTerrainToColorMapRenderer(component, scene);
        EditorUtility.SetDirty(go);
        if (component != null)
            EditorUtility.SetDirty(component);

        return component;
    }

    private static System.Type FindStylizedGrassColorMapRendererType()
    {
        System.Type fallback = null;
        foreach (System.Type type in TypeCache.GetTypesDerivedFrom<Component>())
        {
            if (type == null || type.IsAbstract)
                continue;

            string fullName = type.FullName ?? "";
            string name = type.Name ?? "";

            bool nameMatches = name == "ColorMapRenderer" || name == "ColormapRenderer" || name.Contains("ColorMapRenderer");
            if (!nameMatches)
                continue;

            bool belongsToStylizedGrass = fullName.IndexOf("stylizedgrass", System.StringComparison.OrdinalIgnoreCase) >= 0
                || fullName.IndexOf("staggart", System.StringComparison.OrdinalIgnoreCase) >= 0
                || fullName.IndexOf("grass", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (belongsToStylizedGrass)
                return type;

            if (fallback == null)
                fallback = type;
        }

        return fallback;
    }

    private static Component FindGrassColorMapRenderer()
    {
        GameObject go = FindInActiveScene(EnvironmentRootName + "/" + GrassColorMapRendererName) ?? FindInActiveScene(GrassColorMapRendererName);
        System.Type rendererType = FindStylizedGrassColorMapRendererType();
        if (go != null && rendererType != null)
            return go.GetComponent(rendererType);

        if (rendererType == null)
            return null;

        UnityEngine.Object[] objects = Object.FindObjectsByType(rendererType, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            Component component = objects[i] as Component;
            if (component != null)
                return component;
        }

        return null;
    }

    private static void TryAssignFirstTerrainToColorMapRenderer(Component component, Scene scene)
    {
        GameObject groundTerrainObject = FindGroundTerrainObjectInScene(scene);
        Terrain terrain = groundTerrainObject != null ? groundTerrainObject.GetComponent<Terrain>() : FindGroundTerrainInScene(scene);
        if (groundTerrainObject == null && terrain != null)
            groundTerrainObject = terrain.gameObject;

        if (component != null && groundTerrainObject != null)
            TryAssignTerrainToColorMapRenderer(component, terrain, groundTerrainObject);
    }

    private static bool TryAssignTerrainToColorMapRenderer(Component component, Terrain terrain, GameObject groundTerrainObject)
    {
        if (component == null || groundTerrainObject == null)
            return false;

        bool changed = false;
        Undo.RecordObject(component, "Assign GroundTerrain To Grass Color Map Renderer");

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        System.Type type = component.GetType();

        foreach (var field in type.GetFields(flags))
        {
            if (field == null || field.IsInitOnly)
                continue;

            if (!NameLooksLikeTerrainSlot(field.Name))
                continue;

            if (terrain != null && field.FieldType == typeof(Terrain))
            {
                field.SetValue(component, terrain);
                changed = true;
            }
            else if (field.FieldType == typeof(GameObject))
            {
                field.SetValue(component, groundTerrainObject);
                changed = true;
            }
            else if (terrain != null && field.FieldType == typeof(Terrain[]))
            {
                field.SetValue(component, new Terrain[] { terrain });
                changed = true;
            }
            else if (field.FieldType == typeof(GameObject[]))
            {
                field.SetValue(component, new GameObject[] { groundTerrainObject });
                changed = true;
            }
            else if (typeof(System.Collections.IList).IsAssignableFrom(field.FieldType))
            {
                object listObj = field.GetValue(component);
                System.Collections.IList list = listObj as System.Collections.IList;
                if (list != null)
                {
                    if (CanListAcceptObject(list, groundTerrainObject))
                    {
                        list.Clear();
                        list.Add(groundTerrainObject);
                        changed = true;
                    }
                    else if (terrain != null && CanListAcceptObject(list, terrain))
                    {
                        list.Clear();
                        list.Add(terrain);
                        changed = true;
                    }
                }
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property == null || !property.CanWrite)
                continue;

            if (!NameLooksLikeTerrainSlot(property.Name))
                continue;

            if (terrain != null && property.PropertyType == typeof(Terrain))
            {
                property.SetValue(component, terrain, null);
                changed = true;
            }
            else if (property.PropertyType == typeof(GameObject))
            {
                property.SetValue(component, groundTerrainObject, null);
                changed = true;
            }
            else if (terrain != null && property.PropertyType == typeof(Terrain[]))
            {
                property.SetValue(component, new Terrain[] { terrain }, null);
                changed = true;
            }
            else if (property.PropertyType == typeof(GameObject[]))
            {
                property.SetValue(component, new GameObject[] { groundTerrainObject }, null);
                changed = true;
            }
        }

        changed |= TryAssignTerrainThroughSerializedObject(component, terrain, groundTerrainObject);

        if (changed)
            EditorUtility.SetDirty(component);

        return changed;
    }

    private static bool TryAssignTerrainThroughSerializedObject(Component component, Terrain terrain, GameObject groundTerrainObject)
    {
        if (component == null || groundTerrainObject == null)
            return false;

        bool changed = false;
        SerializedObject so = new SerializedObject(component);
        SerializedProperty iterator = so.GetIterator();
        bool enterChildren = true;
        while (iterator.NextVisible(enterChildren))
        {
            enterChildren = false;
            string propertyName = iterator.name ?? "";
            string displayName = iterator.displayName ?? "";
            bool terrainNamed = NameLooksLikeTerrainSlot(propertyName) || NameLooksLikeTerrainSlot(displayName);
            if (!terrainNamed)
                continue;

            if (iterator.propertyType == SerializedPropertyType.ObjectReference)
            {
                Object target = GetBestTerrainReferenceForSerializedProperty(iterator, terrain, groundTerrainObject);
                if (target == null || iterator.objectReferenceValue == target)
                    continue;

                iterator.objectReferenceValue = target;
                changed = true;
            }
            else if (iterator.isArray && iterator.propertyType == SerializedPropertyType.Generic)
            {
                iterator.arraySize = 1;
                SerializedProperty element = iterator.GetArrayElementAtIndex(0);
                if (element != null && element.propertyType == SerializedPropertyType.ObjectReference)
                {
                    Object target = GetBestTerrainReferenceForSerializedProperty(element, terrain, groundTerrainObject);
                    if (target != null && element.objectReferenceValue != target)
                    {
                        element.objectReferenceValue = target;
                        changed = true;
                    }
                }
            }
        }

        if (changed)
            so.ApplyModifiedPropertiesWithoutUndo();

        return changed;
    }

    private static Object GetBestTerrainReferenceForSerializedProperty(SerializedProperty property, Terrain terrain, GameObject groundTerrainObject)
    {
        if (property == null)
            return groundTerrainObject;

        string type = property.type ?? "";
        string displayName = property.displayName ?? "";
        string name = property.name ?? "";

        if (terrain != null &&
            (type.IndexOf("Terrain", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
             displayName.IndexOf("Terrain Component", System.StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return terrain;
        }

        // Staggart Stylized Grass Color Map Renderer exposes Terrain(s) as GameObject references.
        // In the Inspector this appears as: Element 0  None (Game Object).
        return groundTerrainObject;
    }

    private static bool TryAdjustGrassColorMapRenderArea(Component component, MapDefinition map, Terrain terrain)
    {
        if (component == null)
            return false;

        bool changed = false;
        Undo.RecordObject(component, "Adjust Grass Color Map Render Area");
        Undo.RecordObject(component.transform, "Adjust Grass Color Map Render Area");

        Bounds bounds = GetColorMapTargetBounds(map, terrain);
        component.transform.position = bounds.center;
        component.transform.rotation = Quaternion.identity;
        component.transform.localScale = Vector3.one;
        changed = true;

        changed |= TrySetVector2LikeMember(component, bounds.size.x, bounds.size.z,
            "size", "areaSize", "renderSize", "renderAreaSize", "boundsSize", "dimensions");
        changed |= TrySetVector3LikeMember(component, bounds.size,
            "boundsSize", "size3D", "renderAreaSize3D", "volumeSize");
        changed |= TrySetVector3LikeMember(component, bounds.center,
            "center", "areaCenter", "renderCenter", "boundsCenter");

        changed |= TryInvokeCommonNoArgMethod(component,
            "CalculateFromTerrains", "CalculateFromTerrain", "CalculateRenderArea", "RecalculateRenderArea",
            "FitToTerrains", "FitToTerrain", "ResizeToTerrains", "ResizeToTerrain", "EncapsulateTerrains", "EncapsulateTerrain");

        if (changed)
        {
            EditorUtility.SetDirty(component.transform);
            EditorUtility.SetDirty(component);
        }

        return changed;
    }

    private static bool TryRenderGrassColorMap(Component component)
    {
        if (component == null)
            return false;

        bool invoked = TryInvokeCommonNoArgMethod(component,
            "Render", "RenderColorMap", "RenderColormap", "Bake", "BakeColorMap", "BakeColormap",
            "UpdateColorMap", "UpdateColormap", "Refresh", "RefreshColorMap", "Generate", "GenerateColorMap");

        if (invoked)
        {
            EditorUtility.SetDirty(component);
            AssetDatabase.SaveAssets();
        }

        return invoked;
    }

    private static bool TryInvokeCommonNoArgMethod(Component component, params string[] methodNames)
    {
        if (component == null || methodNames == null)
            return false;

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        System.Type type = component.GetType();

        for (int i = 0; i < methodNames.Length; i++)
        {
            string wanted = methodNames[i];
            var methods = type.GetMethods(flags);
            for (int m = 0; m < methods.Length; m++)
            {
                var method = methods[m];
                if (method == null || method.GetParameters().Length != 0)
                    continue;

                if (!string.Equals(method.Name, wanted, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Undo.RecordObject(component, "Invoke Grass Color Map Method");
                    method.Invoke(component, null);
                    Debug.Log($"[SkyPrisonMapEnvironment] 已调用草 Color Map 方法：{type.FullName}.{method.Name}()", component);
                    return true;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[SkyPrisonMapEnvironment] 调用草 Color Map 方法失败：{type.FullName}.{method.Name}()\n{ex.GetBaseException().Message}", component);
                }
            }
        }

        return false;
    }

    private static bool TrySetVector2LikeMember(Component component, float x, float y, params string[] names)
    {
        if (component == null || names == null)
            return false;

        bool changed = false;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        System.Type type = component.GetType();
        Vector2 value2 = new Vector2(x, y);
        Vector3 value3 = new Vector3(x, 0f, y);

        foreach (var field in type.GetFields(flags))
        {
            if (field == null || field.IsInitOnly || !NameInList(field.Name, names))
                continue;

            if (field.FieldType == typeof(Vector2))
            {
                field.SetValue(component, value2);
                changed = true;
            }
            else if (field.FieldType == typeof(Vector3))
            {
                field.SetValue(component, value3);
                changed = true;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property == null || !property.CanWrite || !NameInList(property.Name, names))
                continue;

            if (property.PropertyType == typeof(Vector2))
            {
                property.SetValue(component, value2, null);
                changed = true;
            }
            else if (property.PropertyType == typeof(Vector3))
            {
                property.SetValue(component, value3, null);
                changed = true;
            }
        }

        return changed;
    }

    private static bool TrySetVector3LikeMember(Component component, Vector3 value, params string[] names)
    {
        if (component == null || names == null)
            return false;

        bool changed = false;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        System.Type type = component.GetType();

        foreach (var field in type.GetFields(flags))
        {
            if (field == null || field.IsInitOnly || !NameInList(field.Name, names))
                continue;

            if (field.FieldType == typeof(Vector3))
            {
                field.SetValue(component, value);
                changed = true;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            if (property == null || !property.CanWrite || !NameInList(property.Name, names))
                continue;

            if (property.PropertyType == typeof(Vector3))
            {
                property.SetValue(component, value, null);
                changed = true;
            }
        }

        return changed;
    }

    private static Bounds GetColorMapTargetBounds(MapDefinition map, Terrain terrain)
    {
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 size = terrain.terrainData.size;
            Vector3 center = terrain.transform.position + new Vector3(size.x * 0.5f, size.y * 0.5f, size.z * 0.5f);
            return new Bounds(center, size);
        }

        if (map != null)
        {
            Vector3 size = map.mapBoundsSize;
            if (size.x <= 0.01f) size.x = 64f;
            if (size.y <= 0.01f) size.y = 6f;
            if (size.z <= 0.01f) size.z = 64f;
            return new Bounds(map.mapBoundsCenter, size);
        }

        return new Bounds(Vector3.zero, new Vector3(64f, 6f, 64f));
    }


    private static GameObject FindGroundTerrainObjectInScene(Scene scene)
    {
        GameObject exact = FindInScene(scene, "GroundTerrain");
        if (exact != null)
            return exact;

        Terrain terrain = FindFirstTerrainInScene(scene);
        return terrain != null ? terrain.gameObject : null;
    }

    private static Terrain FindGroundTerrainInScene(Scene scene)
    {
        GameObject groundTerrainObject = FindInScene(scene, "GroundTerrain");
        if (groundTerrainObject != null)
        {
            Terrain exact = groundTerrainObject.GetComponent<Terrain>();
            if (exact != null)
                return exact;
        }

        return FindFirstTerrainInScene(scene);
    }

    private static Terrain FindFirstTerrainInScene(Scene scene)
    {
        Terrain[] terrains = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
        for (int i = 0; i < terrains.Length; i++)
        {
            Terrain terrain = terrains[i];
            if (terrain == null)
                continue;

            if (!scene.IsValid() || terrain.gameObject.scene == scene)
                return terrain;
        }

        return null;
    }

    private static bool NameLooksLikeTerrainSlot(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.IndexOf("terrain", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool NameInList(string name, string[] list)
    {
        if (string.IsNullOrWhiteSpace(name) || list == null)
            return false;

        for (int i = 0; i < list.Length; i++)
        {
            if (string.Equals(name, list[i], System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool CanListAcceptObject(System.Collections.IList list, Object value)
    {
        if (list == null || value == null)
            return false;

        try
        {
            list.Add(value);
            list.Remove(value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveWrongEnvironmentPointLight(Transform root)
    {
        if (root == null)
            return;

        Light[] lights = root.GetComponentsInChildren<Light>(true);
        for (int i = lights.Length - 1; i >= 0; i--)
        {
            Light light = lights[i];
            if (light == null)
                continue;

            if (light.type == LightType.Point && IsEnvironmentNamedObject(light.name))
                Undo.DestroyObjectImmediate(light.gameObject);
        }
    }

    private static bool IsEnvironmentNamedObject(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return name.Contains("Environment") || name.Contains("Ambient") || name.Contains("环境");
    }

    private static Light FindEnvironmentAreaLight()
    {
        GameObject go = FindInActiveScene(EnvironmentRootName + "/" + EnvironmentAreaLightName) ?? FindInActiveScene(EnvironmentAreaLightName);
        if (go != null)
        {
            Light l = go.GetComponent<Light>();
            if (l != null)
                return l;
        }

        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            Light light = lights[i];
            if (light != null && light.type == LightType.Rectangle && IsEnvironmentNamedObject(light.name))
                return light;
        }

        return null;
    }

    private static Light FindMainDirectionalLight()
    {
        GameObject go = FindInActiveScene(EnvironmentRootName + "/" + MainLightName) ?? FindInActiveScene(MainLightName) ?? FindInActiveScene("Directional Light");
        if (go != null)
        {
            Light l = go.GetComponent<Light>();
            if (l != null && l.type == LightType.Directional)
                return l;
        }

        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null && lights[i].type == LightType.Directional)
                return lights[i];
        }

        return null;
    }

    private static Volume FindPostProcessVolume()
    {
        GameObject go = FindInActiveScene(EnvironmentRootName + "/" + PostProcessVolumeName) ?? FindInActiveScene(PostProcessVolumeName);
        if (go != null)
            return go.GetComponent<Volume>();

        Volume[] volumes = Object.FindObjectsByType<Volume>(FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i] != null && volumes[i].isGlobal)
                return volumes[i];
        }

        return null;
    }

    private static GameObject FindInActiveScene(string pathOrName)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (pathOrName.Contains("/"))
        {
            string[] parts = pathOrName.Split('/');
            if (parts.Length == 0)
                return null;

            GameObject root = FindInScene(scene, parts[0]);
            Transform current = root != null ? root.transform : null;
            for (int i = 1; current != null && i < parts.Length; i++)
                current = current.Find(parts[i]);

            return current != null ? current.gameObject : null;
        }

        return FindInScene(scene, pathOrName);
    }

    private static GameObject FindInScene(Scene scene, string name)
    {
        if (!scene.IsValid() || string.IsNullOrWhiteSpace(name))
            return GameObject.Find(name);

        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject found = FindChildByNameRecursive(roots[i].transform, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static GameObject FindChildByNameRecursive(Transform root, string name)
    {
        if (root == null)
            return null;

        if (root.name == name)
            return root.gameObject;

        for (int i = 0; i < root.childCount; i++)
        {
            GameObject found = FindChildByNameRecursive(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
