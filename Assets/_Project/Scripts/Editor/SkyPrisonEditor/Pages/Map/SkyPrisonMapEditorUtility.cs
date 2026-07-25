using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SkyPrisonMapEditorUtility
{
    private const string GroundPhysicsObjectName = "GroundPhysics";
    private const string GroundPhysicsLayerName = "GroundPhysics";
    private const int GroundPhysicsLayerIndex = 21;
    private const string GroundTerrainObjectName = "GroundTerrain";
    private const string GroundTerrainDataFolderName = "Terrain";
    private const string World3DLayerName = "World3D";
    private const int World3DLayerIndex = 7;

    // 简化版：
    // 1. 直接在目标文件夹里生成 MapDefinition 和 Scene
    // 2. 不再额外创建 new_map/Scenes 这类嵌套目录
    // 3. 左侧树把“包含 MapDefinition 的文件夹”视为地图包目录

    public static MapDefinition CreateMap(SkyPrisonCreateMapWindow.CreateMapResult result, string targetFolder)
    {
        string categoryFolder = string.IsNullOrWhiteSpace(targetFolder)
            ? SkyPrisonMapEditorPage.DefaultMapCreateFolder
            : targetFolder.Replace("\\", "/").TrimEnd('/');

        EnsureFolderExists(categoryFolder);

        // 文件名只来自 CreateMapWindow 的“文件名称”，不能使用主语言名称。
        // 主语言名称只用于游戏内显示，避免生成中文文件夹 / 中文 Scene 文件。
        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(result.fileName) ? "NewMap" : result.fileName);
        string safeKey = string.IsNullOrWhiteSpace(result.mapKey) ? GenerateKeyFromFileName(safeName) : result.mapKey;

        // 地图包规则：分类目录 / 地图包文件夹 / MD_xxx.asset + xxx.unity。
        // 左侧树是用 MD_xxx.asset 所在文件夹来识别地图包的。
        // 不使用 GenerateUniqueAssetPath 自动生成 NewMap 1 / NewMap 2；重名时直接提示用户改文件名。
        string packageFolder = $"{categoryFolder}/{safeName}";
        if (AssetDatabase.IsValidFolder(packageFolder) || File.Exists(packageFolder) || Directory.Exists(packageFolder))
        {
            EditorUtility.DisplayDialog(
                "地图已存在",
                $"目标目录下已经存在地图包：\n{packageFolder}\n\n请修改“文件名称”后再创建。",
                "确定");
            return null;
        }

        EnsureFolderExists(packageFolder);

        string mapAssetPath = $"{packageFolder}/MD_{safeName}.asset";
        string scenePath = $"{packageFolder}/{safeName}.unity";

        MapDefinition map = ScriptableObject.CreateInstance<MapDefinition>();
        map.mapKey = safeKey;
        map.fileName = safeName;
        map.displayName = string.IsNullOrWhiteSpace(result.mapName) ? safeName : result.mapName.Trim();
        map.description = result.mapDescription ?? "";
        map.localizedNames = CloneLocalizedList(result.localizedNames);
        map.localizedDescriptions = CloneLocalizedList(result.localizedDescriptions);
        map.mapBoundsCenter = Vector3.zero;
        map.mapBoundsSize = new Vector3(result.mapSizeXZ.x, 6f, result.mapSizeXZ.y);
        map.enableFogOfWar = result.enableFogOfWar;
        map.enableDayNightCycle = result.enableDayNightCycle;
        map.enableWeather = result.enableWeather;
        map.weatherType = result.weatherType;
        map.scenePath = scenePath;

        AssetDatabase.CreateAsset(map, mapAssetPath);
        AssetDatabase.ImportAsset(mapAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.SaveAssets();

        CreateSceneFromTemplateOrFallback(map, safeName, scenePath);

        map.scenePath = scenePath;
        map.sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
        EditorUtility.SetDirty(map);

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(mapAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        return AssetDatabase.LoadAssetAtPath<MapDefinition>(mapAssetPath);
    }

    private static void CreateSceneFromTemplateOrFallback(MapDefinition map, string safeName, string scenePath)
    {
        const string templateScenePath = "Assets/_Project/Maps/_Templates/MapTemplate_Base/MapTemplate_Base.unity";
        bool copiedTemplate = false;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(templateScenePath) != null)
        {
            copiedTemplate = AssetDatabase.CopyAsset(templateScenePath, scenePath);
            if (copiedTemplate)
                AssetDatabase.ImportAsset(scenePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            else
                Debug.LogWarning("[SkyPrisonMapEditorUtility] Template scene copy failed. Fallback to generated default scene.");
        }

        Scene scene;
        if (copiedTemplate)
        {
            scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            scene.name = safeName;
            ApplyMapSettingsToOpenedScene(map);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        else
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = safeName;
            BuildDefaultSceneHierarchy(map, scene);
            EditorSceneManager.SaveScene(scene, scenePath);
        }
    }

    private static void ApplyMapSettingsToOpenedScene(MapDefinition map)
    {
        if (map == null)
            return;

        SkyPrisonMapBounds bounds = Object.FindFirstObjectByType<SkyPrisonMapBounds>();
        if (bounds != null)
        {
            bounds.sourceMode = SkyPrisonMapBounds.BoundsSourceMode.Manual;
            bounds.center = map.mapBoundsCenter;
            bounds.size = map.mapBoundsSize;
            bounds.RefreshBounds();
            EditorUtility.SetDirty(bounds);
        }

        Scene activeScene = EditorSceneManager.GetActiveScene();
        GameObject worldRoot = FindOrCreateRootObject("WorldRoot", activeScene);
        EnsureGroundTerrainToMap(map, worldRoot.transform);

        SyncFogOverlayToMap(map);
        EnsureFogOfWarLayerAndGamePlayCamera();
        SkyPrisonMapDepthOfFieldEditorUtility.ApplyDepthOfFieldToScene(map, activeScene, false);
        SkyPrisonMapEnvironmentEditorUtility.ApplyEnvironmentToScene(map, activeScene, false);

        if (activeScene.IsValid())
            EditorSceneManager.MarkSceneDirty(activeScene);
    }


    public static bool RenameMapPackage(MapDefinition map, string newFileName)
    {
        if (map == null)
            return false;

        string safeName = SanitizeFileName(string.IsNullOrWhiteSpace(newFileName) ? map.fileName : newFileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "NewMap";

        string assetPath = AssetDatabase.GetAssetPath(map).Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(assetPath))
            return false;

        string packageFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(packageFolder) || !AssetDatabase.IsValidFolder(packageFolder))
            return false;

        string oldPackageName = Path.GetFileName(packageFolder);
        if (safeName != oldPackageName)
        {
            string error = AssetDatabase.RenameAsset(packageFolder, safeName);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Debug.LogError("[SkyPrisonMapEditorUtility] Rename map package failed: " + error);
                return false;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            packageFolder = (Path.GetDirectoryName(packageFolder)?.Replace("\\", "/") ?? "") + "/" + safeName;
            assetPath = AssetDatabase.GetAssetPath(map).Replace("\\", "/");
        }

        assetPath = AssetDatabase.GetAssetPath(map).Replace("\\", "/");
        if (!string.IsNullOrWhiteSpace(assetPath))
        {
            string desiredAssetName = "MD_" + safeName;
            string currentAssetName = Path.GetFileNameWithoutExtension(assetPath);
            if (currentAssetName != desiredAssetName)
            {
                string error = AssetDatabase.RenameAsset(assetPath, desiredAssetName);
                if (!string.IsNullOrWhiteSpace(error))
                    Debug.LogError("[SkyPrisonMapEditorUtility] Rename map definition asset failed: " + error);
            }
        }

        string scenePath = ResolveMapScenePath(map, true);
        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            string currentSceneName = Path.GetFileNameWithoutExtension(scenePath);
            if (currentSceneName != safeName)
            {
                string error = AssetDatabase.RenameAsset(scenePath, safeName);
                if (!string.IsNullOrWhiteSpace(error))
                    Debug.LogError("[SkyPrisonMapEditorUtility] Rename map scene failed: " + error);
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        map.fileName = safeName;
        RefreshSceneBindingInsideMovedPackage(packageFolder, map);
        EditorUtility.SetDirty(map);
        AssetDatabase.SaveAssets();
        return true;
    }

    public static bool MoveMapPackageToCategoryFolder(MapDefinition map, string categoryFolder)
    {
        return MoveMapPackageToCategoryFolder(map, categoryFolder, out _);
    }

    public static bool MoveMapPackageToCategoryFolder(MapDefinition map, string categoryFolder, out MapDefinition movedMap)
    {
        movedMap = map;

        if (map == null || string.IsNullOrWhiteSpace(categoryFolder))
            return false;

        categoryFolder = categoryFolder.Replace("\\", "/").TrimEnd('/');
        if (!AssetDatabase.IsValidFolder(categoryFolder))
        {
            EditorUtility.DisplayDialog("移动地图失败", "目标文件夹不存在：\n" + categoryFolder, "知道了");
            return false;
        }

        if (!categoryFolder.StartsWith(SkyPrisonMapEditorPage.MapDefinitionRootFolder))
        {
            EditorUtility.DisplayDialog("移动地图失败", "地图只能移动到地图根目录下面的分类文件夹。", "知道了");
            return false;
        }

        string relativeTarget = categoryFolder.Substring(SkyPrisonMapEditorPage.MapDefinitionRootFolder.Length).Trim('/');
        if (relativeTarget == "_Templates" || relativeTarget.StartsWith("_Templates/"))
        {
            EditorUtility.DisplayDialog("移动地图失败", "不能把正式地图移动到 _Templates 模板目录。", "知道了");
            return false;
        }

        string sourceAssetPath = AssetDatabase.GetAssetPath(map).Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(sourceAssetPath))
            return false;

        string sourcePackageFolder = Path.GetDirectoryName(sourceAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(sourcePackageFolder) || !AssetDatabase.IsValidFolder(sourcePackageFolder))
            return false;

        string currentParent = Path.GetDirectoryName(sourcePackageFolder)?.Replace("\\", "/");
        if (categoryFolder == currentParent)
            return false;

        if (categoryFolder == sourcePackageFolder || categoryFolder.StartsWith(sourcePackageFolder + "/"))
        {
            EditorUtility.DisplayDialog("移动地图失败", "不能把地图包移动到它自己的内部。", "知道了");
            return false;
        }

        string packageName = Path.GetFileName(sourcePackageFolder);
        string targetPackageFolder = categoryFolder + "/" + packageName;
        if (AssetDatabase.IsValidFolder(targetPackageFolder) || File.Exists(targetPackageFolder) || Directory.Exists(targetPackageFolder))
        {
            EditorUtility.DisplayDialog(
                "移动地图失败",
                "目标目录已经存在同名地图包：\n" + targetPackageFolder + "\n\n请先重命名地图包，或选择其它目标文件夹。",
                "知道了");
            return false;
        }

        string error = AssetDatabase.MoveAsset(sourcePackageFolder, targetPackageFolder);
        if (!string.IsNullOrWhiteSpace(error))
        {
            Debug.LogError("[SkyPrisonMapEditorUtility] Move package folder failed: " + error);
            EditorUtility.DisplayDialog("移动地图失败", error, "知道了");
            return false;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { targetPackageFolder });
        if (guids != null && guids.Length > 0)
        {
            string movedPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            MapDefinition reloaded = AssetDatabase.LoadAssetAtPath<MapDefinition>(movedPath);
            if (reloaded != null)
                movedMap = reloaded;
        }

        RefreshSceneBindingInsideMovedPackage(targetPackageFolder, movedMap);
        if (movedMap != null)
            EditorUtility.SetDirty(movedMap);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        return true;
    }

    public static void DuplicateMapPackageToCategoryFolder(MapDefinition source, string categoryFolder)
    {
        if (source == null || string.IsNullOrWhiteSpace(categoryFolder))
            return;

        EnsureFolderExists(categoryFolder);

        string sourceAssetPath = AssetDatabase.GetAssetPath(source);
        if (string.IsNullOrWhiteSpace(sourceAssetPath))
            return;

        string sourcePackageFolder = Path.GetDirectoryName(sourceAssetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(sourcePackageFolder))
            return;

        string targetPackageFolder = AssetDatabase.GenerateUniqueAssetPath(categoryFolder.TrimEnd('/') + "/" + Path.GetFileName(sourcePackageFolder));
        FileUtil.CopyFileOrDirectory(sourcePackageFolder, targetPackageFolder);
        string srcMeta = sourcePackageFolder + ".meta";
        if (File.Exists(srcMeta))
            FileUtil.CopyFileOrDirectory(srcMeta, targetPackageFolder + ".meta");

        AssetDatabase.Refresh();

        string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { targetPackageFolder });
        if (guids.Length > 0)
        {
            MapDefinition duplicated = AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (duplicated != null)
            {
                duplicated.mapKey = GenerateUniqueMapKey(source.mapKey + "_copy");
                duplicated.displayName = string.IsNullOrWhiteSpace(source.displayName) ? Path.GetFileName(targetPackageFolder) : source.displayName + "_Copy";
                RefreshSceneBindingInsideMovedPackage(targetPackageFolder, duplicated);
                EditorUtility.SetDirty(duplicated);
                AssetDatabase.SaveAssets();
            }
        }

        AssetDatabase.Refresh();
    }

    public static void DeleteMapPackage(MapDefinition map)
    {
        if (map == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(map);
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        string packageFolder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(packageFolder))
            return;

        AssetDatabase.DeleteAsset(packageFolder);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void RefreshSceneBindingInsideMovedPackage(string packageFolder, MapDefinition explicitMap = null)
    {
        MapDefinition map = explicitMap;
        if (map == null)
        {
            string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { packageFolder });
            if (guids.Length > 0)
                map = AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        if (map == null)
            return;

        string[] sceneGuids = AssetDatabase.FindAssets("t:SceneAsset", new[] { packageFolder });
        if (sceneGuids.Length > 0)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
            map.scenePath = scenePath;
            map.sceneGuid = AssetDatabase.AssetPathToGUID(scenePath);
        }
        else
        {
            map.scenePath = "";
            map.sceneGuid = "";
        }
    }

    public static bool OpenMapScene(MapDefinition map)
    {
        string scenePath = ResolveMapScenePath(map, true);
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            EditorUtility.DisplayDialog(
                "打开地图失败",
                "当前地图没有绑定有效 Scene，并且地图包内也没有找到 .unity 文件。",
                "知道了");
            return false;
        }

        SceneAsset sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (sceneAsset == null)
        {
            EditorUtility.DisplayDialog(
                "打开地图失败",
                "Scene 路径无效：\n" + scenePath,
                "知道了");
            return false;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        try
        {
            Scene openedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            if (openedScene.IsValid())
                SceneManager.SetActiveScene(openedScene);

            Selection.activeObject = sceneAsset;
            EditorGUIUtility.PingObject(sceneAsset);
            return true;
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[SkyPrisonMapEditorUtility] Open map scene failed: " + ex);
            EditorUtility.DisplayDialog(
                "打开地图失败",
                "打开 Scene 时发生错误：\n" + scenePath + "\n\n" + ex.Message,
                "知道了");
            return false;
        }
    }

    public static void PingMapPackage(MapDefinition map)
    {
        if (map == null)
            return;

        string assetPath = AssetDatabase.GetAssetPath(map);
        string packageFolder = string.IsNullOrWhiteSpace(assetPath) ? "" : Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        Object folderObj = !string.IsNullOrWhiteSpace(packageFolder) ? AssetDatabase.LoadAssetAtPath<Object>(packageFolder) : null;
        Selection.activeObject = folderObj != null ? folderObj : map;
        EditorGUIUtility.PingObject(Selection.activeObject);
    }

    public static void PingMapScene(MapDefinition map)
    {
        string scenePath = ResolveMapScenePath(map, false);
        SceneAsset sceneAsset = string.IsNullOrWhiteSpace(scenePath) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
        if (sceneAsset != null)
        {
            Selection.activeObject = sceneAsset;
            EditorGUIUtility.PingObject(sceneAsset);
            return;
        }

        if (map != null)
        {
            Selection.activeObject = map;
            EditorGUIUtility.PingObject(map);
        }
    }

    public static string ResolveMapScenePath(MapDefinition map, bool repairBinding)
    {
        if (map == null)
            return "";

        string scenePath = (map.scenePath ?? "").Replace("\\", "/");
        if (!string.IsNullOrWhiteSpace(scenePath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null)
            return scenePath;

        string assetPath = AssetDatabase.GetAssetPath(map);
        string packageFolder = string.IsNullOrWhiteSpace(assetPath) ? "" : Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
        if (string.IsNullOrWhiteSpace(packageFolder) || !AssetDatabase.IsValidFolder(packageFolder))
            return "";

        string[] sceneGuids = AssetDatabase.FindAssets("t:SceneAsset", new[] { packageFolder });
        if (sceneGuids == null || sceneGuids.Length == 0)
            return "";

        string packageName = Path.GetFileName(packageFolder);
        string selectedPath = "";
        for (int i = 0; i < sceneGuids.Length; i++)
        {
            string p = AssetDatabase.GUIDToAssetPath(sceneGuids[i]).Replace("\\", "/");
            if (Path.GetFileNameWithoutExtension(p) == packageName)
            {
                selectedPath = p;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(selectedPath))
            selectedPath = AssetDatabase.GUIDToAssetPath(sceneGuids[0]).Replace("\\", "/");

        if (repairBinding && !string.IsNullOrWhiteSpace(selectedPath))
        {
            map.scenePath = selectedPath;
            map.sceneGuid = AssetDatabase.AssetPathToGUID(selectedPath);
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
        }

        return selectedPath;
    }

    private static string GenerateUniqueMapKey(string baseKey)
    {
        string key = string.IsNullOrWhiteSpace(baseKey) ? "new_map" : baseKey;
        int suffix = 1;
        string result = key;

        while (true)
        {
            bool exists = false;
            string[] guids = AssetDatabase.FindAssets("t:MapDefinition", new[] { SkyPrisonMapEditorPage.MapDefinitionRootFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                MapDefinition map = AssetDatabase.LoadAssetAtPath<MapDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (map != null && map.mapKey == result)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
                return result;

            result = key + "_" + suffix;
            suffix++;
        }
    }

    private static string GenerateKeyFromFileName(string raw)
    {
        string value = string.IsNullOrWhiteSpace(raw) ? "new_map" : raw.Trim().ToLowerInvariant();
        System.Text.StringBuilder sb = new System.Text.StringBuilder(value.Length);
        bool lastUnderscore = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool valid = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (valid)
            {
                sb.Append(c);
                lastUnderscore = false;
            }
            else if (!lastUnderscore)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }

        string key = sb.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(key) ? "new_map" : key;
    }

    private static string SanitizeFileName(string raw)
    {
        string value = string.IsNullOrWhiteSpace(raw) ? "NewMap" : raw.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
            value = value.Replace(c.ToString(), "_");
        return string.IsNullOrWhiteSpace(value) ? "NewMap" : value;
    }

    private static List<LocalizedTextEntry> CloneLocalizedList(List<LocalizedTextEntry> source)
    {
        List<LocalizedTextEntry> result = new List<LocalizedTextEntry>();
        if (source == null)
            return result;

        for (int i = 0; i < source.Count; i++)
        {
            LocalizedTextEntry entry = source[i];
            if (entry == null)
                continue;

            result.Add(new LocalizedTextEntry
            {
                languageCode = entry.languageCode,
                text = entry.text ?? ""
            });
        }

        return result;
    }

    public static void BuildDefaultSceneHierarchy(MapDefinition map, Scene scene)
    {
        GameObject system = new GameObject("System");
        GameObject cameraSystem = new GameObject("CameraSystem");
        GameObject renderSystem = new GameObject("RenderSystem");
        GameObject worldLogic = new GameObject("WorldLogic");
        GameObject visionSystem = new GameObject("VisionSystem");
        GameObject mapBounds = new GameObject("MapBounds");

        cameraSystem.transform.SetParent(system.transform);
        renderSystem.transform.SetParent(system.transform);
        worldLogic.transform.SetParent(system.transform);
        visionSystem.transform.SetParent(system.transform);
        mapBounds.transform.SetParent(system.transform);

        GameObject unitRoot = new GameObject("UnitRoot");
        new GameObject("PlayerRoot").transform.SetParent(unitRoot.transform);
        new GameObject("EnemyRoot").transform.SetParent(unitRoot.transform);
        new GameObject("NeutralRoot").transform.SetParent(unitRoot.transform);
        new GameObject("DestructibleRoot").transform.SetParent(unitRoot.transform);
        new GameObject("LootRoot").transform.SetParent(unitRoot.transform);

        GameObject worldRoot = new GameObject("WorldRoot");
        GameObject groundRoot = new GameObject("GroundRoot");
        GameObject structureRoot = new GameObject("StructureRoot");
        GameObject terrainPropRoot = new GameObject("TerrainPropRoot");
        GameObject pathDecorationRoot = new GameObject("PathDecorationRoot");
        GameObject backgroundRoot = new GameObject("BackgroundRoot");
        GameObject sortableRoot = new GameObject("SortableRoot");
        GameObject frontOccluderRoot = new GameObject("FrontOccluderRoot");
        GameObject vfxRoot = new GameObject("VFXRoot");
        groundRoot.transform.SetParent(worldRoot.transform);
        structureRoot.transform.SetParent(worldRoot.transform);
        terrainPropRoot.transform.SetParent(worldRoot.transform);
        pathDecorationRoot.transform.SetParent(worldRoot.transform);
        backgroundRoot.transform.SetParent(worldRoot.transform);
        sortableRoot.transform.SetParent(worldRoot.transform);
        frontOccluderRoot.transform.SetParent(worldRoot.transform);
        vfxRoot.transform.SetParent(worldRoot.transform);

        GameObject debugRoot = new GameObject("DebugRoot");
        GameObject spine = new GameObject("Spine");
        GameObject canvas = new GameObject("Canvas");
        GameObject eventSystem = new GameObject("EventSystem");
        GameObject screenOutlineSystem = new GameObject("ScreenSpaceOutlineSystem");

        EditorSceneManager.MoveGameObjectToScene(system, scene);
        EditorSceneManager.MoveGameObjectToScene(unitRoot, scene);
        EditorSceneManager.MoveGameObjectToScene(worldRoot, scene);
        EditorSceneManager.MoveGameObjectToScene(debugRoot, scene);
        EditorSceneManager.MoveGameObjectToScene(spine, scene);
        EditorSceneManager.MoveGameObjectToScene(canvas, scene);
        EditorSceneManager.MoveGameObjectToScene(eventSystem, scene);
        EditorSceneManager.MoveGameObjectToScene(screenOutlineSystem, scene);

        SkyPrisonMapBounds bounds = mapBounds.AddComponent<SkyPrisonMapBounds>();
        bounds.sourceMode = SkyPrisonMapBounds.BoundsSourceMode.Manual;
        bounds.center = map != null ? map.mapBoundsCenter : Vector3.zero;
        bounds.size = map != null ? map.mapBoundsSize : new Vector3(64f, 6f, 64f);

        SkyPrisonVisionManager visionMgr = visionSystem.AddComponent<SkyPrisonVisionManager>();
        visionMgr.RebuildCache();

        GameObject fogOverlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fogOverlay.name = "FogOverlay";
        ApplyFogOfWarLayerToObject(fogOverlay);
        fogOverlay.transform.SetParent(visionSystem.transform);
        fogOverlay.transform.position = new Vector3(0f, 0.08f, 0f);
        fogOverlay.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        fogOverlay.transform.localScale = new Vector3(map != null ? map.mapBoundsSize.x : 64f, map != null ? map.mapBoundsSize.z : 64f, 1f);

        Collider overlayCollider = fogOverlay.GetComponent<Collider>();
        if (overlayCollider != null)
            Object.DestroyImmediate(overlayCollider);

        MeshRenderer overlayRenderer = fogOverlay.GetComponent<MeshRenderer>();
        Material fogMat = FindOrCreateFogMaterial();
        if (overlayRenderer != null && fogMat != null)
            overlayRenderer.sharedMaterial = fogMat;

        SkyPrisonFogMaskRenderer fogMask = fogOverlay.AddComponent<SkyPrisonFogMaskRenderer>();
        if (map != null)
            fogMask.ResolveReferences();

        EnsureGroundTerrainToMap(map, worldRoot.transform);
        EnsureFogOfWarLayerAndGamePlayCamera();
        SkyPrisonMapDepthOfFieldEditorUtility.ApplyDepthOfFieldToScene(map, scene, false);
        SkyPrisonMapEnvironmentEditorUtility.ApplyEnvironmentToScene(map, scene, false);
    }

    /// <summary>
    /// 校对并补齐地图基础节点。已有节点和组件一律保留，只补缺失项。
    /// 这里也会强制校对战争迷雾层级：FogOverlay = 20:FogOfWar，GamePlayCamera CullingMask 包含 FogOfWar。
    /// </summary>
    public static void EnsureDefaultSceneHierarchy(MapDefinition map, Scene scene)
    {
        if (!scene.IsValid())
            scene = EditorSceneManager.GetActiveScene();

        GameObject system = FindOrCreateRootObject("System", scene);
        GameObject cameraSystem = FindOrCreateChildObject(system.transform, "CameraSystem");
        GameObject renderSystem = FindOrCreateChildObject(system.transform, "RenderSystem");
        GameObject worldLogic = FindOrCreateChildObject(system.transform, "WorldLogic");
        GameObject visionSystem = FindOrCreateChildObject(system.transform, "VisionSystem");
        GameObject mapBoundsObject = FindOrCreateChildObject(system.transform, "MapBounds");

        SkyPrisonMapBounds bounds = mapBoundsObject.GetComponent<SkyPrisonMapBounds>();
        if (bounds == null)
            bounds = mapBoundsObject.AddComponent<SkyPrisonMapBounds>();

        bounds.sourceMode = SkyPrisonMapBounds.BoundsSourceMode.Manual;
        if (map != null)
        {
            bounds.center = map.mapBoundsCenter;
            bounds.size = map.mapBoundsSize;
        }
        bounds.RefreshBounds();
        EditorUtility.SetDirty(bounds);

        SkyPrisonVisionManager visionManager = visionSystem.GetComponent<SkyPrisonVisionManager>();
        if (visionManager == null)
            visionManager = visionSystem.AddComponent<SkyPrisonVisionManager>();
        EditorUtility.SetDirty(visionManager);

        GameObject fogOverlay = GameObject.Find("FogOverlay");
        if (fogOverlay == null || fogOverlay.transform.parent != visionSystem.transform)
            fogOverlay = FindOrCreateChildObject(visionSystem.transform, "FogOverlay");

        ApplyFogOfWarLayerToObject(fogOverlay);
        fogOverlay.transform.position = new Vector3(
            map != null ? map.mapBoundsCenter.x : 0f,
            (map != null ? map.mapBoundsCenter.y : 0f) + 0.08f,
            map != null ? map.mapBoundsCenter.z : 0f);
        fogOverlay.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        fogOverlay.transform.localScale = new Vector3(
            map != null ? map.mapBoundsSize.x : 64f,
            map != null ? map.mapBoundsSize.z : 64f,
            1f);

        MeshFilter meshFilter = fogOverlay.GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = fogOverlay.AddComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        MeshRenderer overlayRenderer = fogOverlay.GetComponent<MeshRenderer>();
        if (overlayRenderer == null)
            overlayRenderer = fogOverlay.AddComponent<MeshRenderer>();
        Material fogMat = FindOrCreateFogMaterial();
        if (fogMat != null)
            overlayRenderer.sharedMaterial = fogMat;

        Collider overlayCollider = fogOverlay.GetComponent<Collider>();
        if (overlayCollider != null)
            Object.DestroyImmediate(overlayCollider);

        SkyPrisonFogMaskRenderer fogMaskRenderer = fogOverlay.GetComponent<SkyPrisonFogMaskRenderer>();
        if (fogMaskRenderer == null)
            fogMaskRenderer = fogOverlay.AddComponent<SkyPrisonFogMaskRenderer>();
        fogMaskRenderer.ResolveReferences();
        EditorUtility.SetDirty(fogOverlay);
        EditorUtility.SetDirty(fogMaskRenderer);

        FindOrCreateRootObject("UnitRoot", scene);
        GameObject worldRoot = FindOrCreateRootObject("WorldRoot", scene);
        FindOrCreateChildObject(worldRoot.transform, "GroundRoot");
        FindOrCreateChildObject(worldRoot.transform, "StructureRoot");
        FindOrCreateChildObject(worldRoot.transform, "TerrainPropRoot");
        FindOrCreateChildObject(worldRoot.transform, "PathDecorationRoot");
        FindOrCreateChildObject(worldRoot.transform, "BackgroundRoot");
        FindOrCreateChildObject(worldRoot.transform, "SortableRoot");
        FindOrCreateChildObject(worldRoot.transform, "FrontOccluderRoot");
        FindOrCreateChildObject(worldRoot.transform, "FXRoot");
        EnsureGroundTerrainToMap(map, worldRoot.transform);
        FindOrCreateRootObject("DebugRoot", scene);
        FindOrCreateRootObject("Canvas", scene);
        FindOrCreateRootObject("EventSystem", scene);
        FindOrCreateRootObject("ScreenSpaceOutlineSystem", scene);

        EnsureFogOfWarLayerAndGamePlayCamera();
        SkyPrisonMapDepthOfFieldEditorUtility.ApplyDepthOfFieldToScene(map, scene, false);
        SkyPrisonMapEnvironmentEditorUtility.ApplyEnvironmentToScene(map, scene, false);

        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);
    }


    public static void SyncBaseGroundBlockToMap(MapDefinition map)
    {
        if (map == null)
            return;

        GameObject worldRoot = GameObject.Find("WorldRoot");
        if (worldRoot == null)
            return;

        EnsureGroundTerrainToMap(map, worldRoot.transform);

        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);
    }

    public static void SyncGroundVisualToMapBounds(MapDefinition map)
    {
        // 兼容旧按钮 / 旧代码入口：地图页面的地面矫正现在只处理 Terrain。
        SyncGroundTerrainToMapBounds(map);
    }

    public static void SyncGroundTerrainToMapBounds(MapDefinition map)
    {
        if (map == null)
        {
            EditorUtility.DisplayDialog("生成 / 矫正 Terrain", "未选择地图定义。", "确定");
            return;
        }

        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("生成 / 矫正 Terrain", "当前 Scene 无效。", "确定");
            return;
        }

        GameObject worldRoot = FindOrCreateRootObject("WorldRoot", scene);
        GameObject terrainObject = EnsureGroundTerrainToMap(map, worldRoot.transform);

        if (terrainObject != null)
        {
            Selection.activeGameObject = terrainObject;
            EditorGUIUtility.PingObject(terrainObject);
            Debug.Log($"[SkyPrisonMapEditorUtility] 已生成 / 矫正 GroundRoot/{GroundTerrainObjectName}：Terrain 对齐 MapBounds，Layer = {World3DLayerIndex}:{World3DLayerName}。{map.name}", terrainObject);
        }

        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static GameObject EnsureGroundTerrainToMap(MapDefinition map, Transform worldRoot)
    {
        if (worldRoot == null)
            return null;

        GameObject groundRoot = FindOrCreateChildObject(worldRoot, "GroundRoot");
        GameObject terrainObject = FindOrCreateChildObject(groundRoot.transform, GroundTerrainObjectName);

        Vector3 center = map != null ? map.mapBoundsCenter : Vector3.zero;
        Vector3 size = map != null ? map.mapBoundsSize : new Vector3(64f, 8f, 64f);
        size.x = Mathf.Max(1f, Mathf.Abs(size.x));
        size.y = Mathf.Max(1f, Mathf.Abs(size.y));
        size.z = Mathf.Max(1f, Mathf.Abs(size.z));

        ApplyWorld3DLayerToObject(terrainObject);

        Undo.RecordObject(terrainObject.transform, "Sync Ground Terrain To Map Bounds");
        terrainObject.transform.position = new Vector3(
            center.x - size.x * 0.5f,
            center.y,
            center.z - size.z * 0.5f);
        terrainObject.transform.rotation = Quaternion.identity;
        terrainObject.transform.localScale = Vector3.one;

        TerrainData terrainData = FindOrCreateTerrainDataAsset(map, size);
        if (terrainData == null)
        {
            Debug.LogError("[SkyPrisonMapEditorUtility] 无法创建 TerrainData，GroundTerrain 生成中止。", terrainObject);
            return terrainObject;
        }

        Undo.RecordObject(terrainData, "Sync Ground TerrainData Size");
        terrainData.size = size;
        terrainData.name = map != null && !string.IsNullOrWhiteSpace(map.mapKey)
            ? $"TD_{map.mapKey}_GroundTerrain"
            : "TD_GroundTerrain";
        terrainData.SyncHeightmap();
        EnsureTerrainHasVisibleDefaultLayer(map, terrainData);
        EnsureTerrainAlphamapsAreValid(terrainData);

        Terrain terrain = terrainObject.GetComponent<Terrain>();
        if (terrain == null)
            terrain = terrainObject.AddComponent<Terrain>();
        Undo.RecordObject(terrain, "Sync Ground Terrain Component");
        terrain.terrainData = terrainData;
        terrain.drawHeightmap = true;
        terrain.drawTreesAndFoliage = true;
        terrain.drawInstanced = true;
        terrain.allowAutoConnect = false;
        EnsureTerrainHasRuntimeVisibleMaterial(map, terrain);

        TerrainCollider collider = terrainObject.GetComponent<TerrainCollider>();
        if (collider == null)
            collider = terrainObject.AddComponent<TerrainCollider>();
        Undo.RecordObject(collider, "Sync Ground Terrain Collider");
        collider.terrainData = terrainData;

        // Terrain 自身就是显示与地面碰撞，不再给地图页补 GroundVisual / GroundPhysics / GroundCollider。
        BaseGroundBlock oldBlock = terrainObject.GetComponent<BaseGroundBlock>();
        if (oldBlock != null)
            Object.DestroyImmediate(oldBlock);

        GroundSurfaceMarker marker = terrainObject.GetComponent<GroundSurfaceMarker>();
        if (marker != null)
        {
            Undo.RecordObject(marker, "Sync Ground Terrain Marker");
            marker.isBaseGround = true;
            EditorUtility.SetDirty(marker);
        }

        EditorUtility.SetDirty(terrainData);
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(terrainObject);
        return terrainObject;
    }

    private static TerrainData FindOrCreateTerrainDataAsset(MapDefinition map, Vector3 size)
    {
        string folder = GetTerrainDataFolder(map);
        EnsureFolderExists(folder);

        string key = map != null && !string.IsNullOrWhiteSpace(map.mapKey) ? SanitizeFileName(map.mapKey) : "GroundTerrain";
        string path = $"{folder}/TD_{key}_GroundTerrain.asset";
        TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        if (data != null)
            return data;

        data = new TerrainData
        {
            heightmapResolution = 129,
            alphamapResolution = 512,
            baseMapResolution = 512,
            size = size
        };

        AssetDatabase.CreateAsset(data, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return AssetDatabase.LoadAssetAtPath<TerrainData>(path);
    }


    private static void EnsureTerrainHasRuntimeVisibleMaterial(MapDefinition map, Terrain terrain)
    {
        if (terrain == null)
            return;

        string folder = GetTerrainDataFolder(map);
        EnsureFolderExists(folder);

        string key = map != null && !string.IsNullOrWhiteSpace(map.mapKey) ? SanitizeFileName(map.mapKey) : "GroundTerrain";
        string materialPath = $"{folder}/MAT_{key}_GroundTerrainVisible.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        Shader shader = FindTerrainVisibleShader();
        if (shader == null)
        {
            // 找不到管线对应 Terrain Shader 时，宁可保留 Unity 默认 Terrain 材质，不强行塞一个错误材质。
            terrain.materialTemplate = null;
            EditorUtility.SetDirty(terrain);
            return;
        }

        if (material == null || material.shader != shader)
        {
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = $"MAT_{key}_GroundTerrainVisible"
                };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = shader;
            }
        }

        // 尽量给不同管线一个偏亮的兜底色，避免没有灯光时看起来像没生成。
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f, 1f));
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", new Color(0.55f, 0.55f, 0.55f, 1f));
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.12f);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        terrain.materialTemplate = material;
        EditorUtility.SetDirty(material);
        EditorUtility.SetDirty(terrain);
        AssetDatabase.SaveAssets();
    }

    private static Shader FindTerrainVisibleShader()
    {
        string[] shaderNames =
        {
            "HDRP/TerrainLit",
            "HDRenderPipeline/TerrainLit",
            "Universal Render Pipeline/Terrain/Lit",
            "Nature/Terrain/Standard",
            "Nature/Terrain/Diffuse"
        };

        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
                return shader;
        }

        return null;
    }

    private static void EnsureTerrainHasVisibleDefaultLayer(MapDefinition map, TerrainData terrainData)
    {
        if (terrainData == null)
            return;

        float defaultDebugTileSize = Mathf.Max(1f, terrainData.size.x, terrainData.size.z);

        TerrainLayer[] layers = terrainData.terrainLayers;
        bool hasUsableLayer = false;
        if (layers != null)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                TerrainLayer layer = layers[i];
                if (layer == null || layer.diffuseTexture == null)
                    continue;

                hasUsableLayer = true;

                // 只矫正我们自己生成的临时调试层；正式地表材质层不要在地图页被擅自改。
                if (IsSkyPrisonDebugTerrainLayer(layer))
                {
                    layer.tileSize = new Vector2(defaultDebugTileSize, defaultDebugTileSize);
                    layer.tileOffset = Vector2.zero;
                    layer.specular = Color.black;
                    layer.metallic = 0f;
                    layer.smoothness = 0.08f;

                    if (layer.diffuseTexture is Texture2D texture)
                    {
                        texture.wrapMode = TextureWrapMode.Repeat;
                        texture.filterMode = FilterMode.Bilinear;
                        texture.anisoLevel = 1;
                        EditorUtility.SetDirty(texture);
                    }

                    EditorUtility.SetDirty(layer);
                }
            }
        }
        if (hasUsableLayer)
        {
            EditorUtility.SetDirty(terrainData);
            AssetDatabase.SaveAssets();
            return;
        }

        string folder = GetTerrainDataFolder(map);
        EnsureFolderExists(folder);

        string key = map != null && !string.IsNullOrWhiteSpace(map.mapKey) ? SanitizeFileName(map.mapKey) : "GroundTerrain";
        string texturePath = $"{folder}/TX_{key}_TerrainDebugGray.asset";
        Texture2D debugTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (debugTexture == null)
        {
            debugTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
            {
                name = $"TX_{key}_TerrainDebugGray",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1
            };

            debugTexture.SetPixel(0, 0, new Color(0.42f, 0.42f, 0.42f, 1f));
            debugTexture.Apply(false, false);

            AssetDatabase.CreateAsset(debugTexture, texturePath);
        }

        string layerPath = $"{folder}/TL_{key}_TerrainDebugGray.terrainlayer";
        TerrainLayer defaultLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (defaultLayer == null)
        {
            defaultLayer = new TerrainLayer
            {
                name = $"TL_{key}_TerrainDebugGray",
                diffuseTexture = debugTexture,
                tileSize = new Vector2(defaultDebugTileSize, defaultDebugTileSize),
                tileOffset = Vector2.zero,
                specular = Color.black,
                metallic = 0f,
                smoothness = 0.08f
            };
            AssetDatabase.CreateAsset(defaultLayer, layerPath);
        }
        else
        {
            defaultLayer.diffuseTexture = debugTexture;
            defaultLayer.tileSize = new Vector2(defaultDebugTileSize, defaultDebugTileSize);
            defaultLayer.tileOffset = Vector2.zero;
            defaultLayer.specular = Color.black;
            defaultLayer.metallic = 0f;
            defaultLayer.smoothness = 0.08f;
            EditorUtility.SetDirty(defaultLayer);
        }

        terrainData.terrainLayers = new[] { defaultLayer };

        int width = Mathf.Max(1, terrainData.alphamapWidth);
        int height = Mathf.Max(1, terrainData.alphamapHeight);
        float[,,] alphamaps = new float[height, width, 1];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                alphamaps[y, x, 0] = 1f;
        }
        terrainData.SetAlphamaps(0, 0, alphamaps);

        EditorUtility.SetDirty(debugTexture);
        EditorUtility.SetDirty(defaultLayer);
        EditorUtility.SetDirty(terrainData);
        AssetDatabase.SaveAssets();
    }

    private static void EnsureTerrainAlphamapsAreValid(TerrainData terrainData)
    {
        if (terrainData == null)
            return;

        TerrainLayer[] layers = terrainData.terrainLayers;
        int layerCount = layers != null ? layers.Length : 0;
        if (layerCount <= 0)
            return;

        int width = Mathf.Max(1, terrainData.alphamapWidth);
        int height = Mathf.Max(1, terrainData.alphamapHeight);
        float[,,] alphamaps = terrainData.GetAlphamaps(0, 0, width, height);

        bool changed = false;
        int fallbackLayer = FindDebugLayerIndex(layers);
        if (fallbackLayer < 0)
            fallbackLayer = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sum = 0f;
                for (int l = 0; l < layerCount; l++)
                {
                    float value = alphamaps[y, x, l];
                    if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                    {
                        alphamaps[y, x, l] = 0f;
                        changed = true;
                        continue;
                    }

                    sum += value;
                }

                if (sum <= 0.0001f)
                {
                    for (int l = 0; l < layerCount; l++)
                        alphamaps[y, x, l] = 0f;

                    alphamaps[y, x, fallbackLayer] = 1f;
                    changed = true;
                }
                else if (Mathf.Abs(sum - 1f) > 0.001f)
                {
                    for (int l = 0; l < layerCount; l++)
                        alphamaps[y, x, l] = alphamaps[y, x, l] / sum;

                    changed = true;
                }
            }
        }

        if (!changed)
            return;

        terrainData.SetAlphamaps(0, 0, alphamaps);
        EditorUtility.SetDirty(terrainData);
        AssetDatabase.SaveAssets();
    }

    private static int FindDebugLayerIndex(TerrainLayer[] layers)
    {
        if (layers == null)
            return -1;

        for (int i = 0; i < layers.Length; i++)
        {
            if (IsSkyPrisonDebugTerrainLayer(layers[i]))
                return i;
        }

        return -1;
    }

    private static bool IsSkyPrisonDebugTerrainLayer(TerrainLayer layer)
    {
        if (layer == null)
            return false;

        string layerName = layer.name ?? string.Empty;
        if (layerName.Contains("TerrainDebugGray") || layerName.Contains("TerrainCleanBaseGray"))
            return true;

        Texture texture = layer.diffuseTexture;
        string textureName = texture != null ? texture.name ?? string.Empty : string.Empty;
        return textureName.Contains("TerrainDebugGray") || textureName.Contains("TerrainCleanBaseGray");
    }

    public static void ResetGroundTerrainSurfaceToCleanDebugLayer(MapDefinition map)
    {
        if (map == null)
        {
            EditorUtility.DisplayDialog("重置 Terrain 地表层", "未选择地图定义。", "确定");
            return;
        }

        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid())
        {
            EditorUtility.DisplayDialog("重置 Terrain 地表层", "当前 Scene 无效。", "确定");
            return;
        }

        GameObject worldRoot = FindOrCreateRootObject("WorldRoot", scene);
        GameObject terrainObject = EnsureGroundTerrainToMap(map, worldRoot.transform);
        Terrain terrain = terrainObject != null ? terrainObject.GetComponent<Terrain>() : null;
        TerrainData terrainData = terrain != null ? terrain.terrainData : null;
        if (terrainData == null)
        {
            EditorUtility.DisplayDialog("重置 Terrain 地表层", "没有找到可用的 GroundTerrain / TerrainData。", "确定");
            return;
        }

        bool ok = EditorUtility.DisplayDialog(
            "重置 Terrain 地表层",
            "这会清空当前 Terrain 的所有地表材质层与刷图权重，并重置为一层干净的灰色底层。\n\n用于清理旧模拟/烘焙残留、脏 alphamap、异常 Debug 层。",
            "重置",
            "取消");
        if (!ok)
            return;

        ResetTerrainSurfaceDataToCleanDebugLayer(map, terrainData);

        EditorUtility.SetDirty(terrainData);
        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(terrainObject);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);

        Selection.activeGameObject = terrainObject;
        EditorGUIUtility.PingObject(terrainObject);
        Debug.Log($"[SkyPrisonMapEditorUtility] 已重置 GroundTerrain 地表层为干净灰色底层：{map.name}", terrainObject);
    }

    private static void ResetTerrainSurfaceDataToCleanDebugLayer(MapDefinition map, TerrainData terrainData)
    {
        if (terrainData == null)
            return;

        string folder = GetTerrainDataFolder(map);
        EnsureFolderExists(folder);

        string key = map != null && !string.IsNullOrWhiteSpace(map.mapKey) ? SanitizeFileName(map.mapKey) : "GroundTerrain";
        string texturePath = $"{folder}/TX_{key}_TerrainCleanBaseGray.asset";
        Texture2D cleanTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (cleanTexture == null)
        {
            cleanTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true)
            {
                name = $"TX_{key}_TerrainCleanBaseGray",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 1
            };
            cleanTexture.SetPixel(0, 0, new Color(0.42f, 0.42f, 0.42f, 1f));
            cleanTexture.Apply(false, false);
            AssetDatabase.CreateAsset(cleanTexture, texturePath);
        }
        else
        {
            cleanTexture.wrapMode = TextureWrapMode.Repeat;
            cleanTexture.filterMode = FilterMode.Bilinear;
            cleanTexture.anisoLevel = 1;
            cleanTexture.SetPixel(0, 0, new Color(0.42f, 0.42f, 0.42f, 1f));
            cleanTexture.Apply(false, false);
            EditorUtility.SetDirty(cleanTexture);
        }

        string layerPath = $"{folder}/TL_{key}_TerrainCleanBaseGray.terrainlayer";
        TerrainLayer cleanLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (cleanLayer == null)
        {
            cleanLayer = new TerrainLayer
            {
                name = $"TL_{key}_TerrainCleanBaseGray"
            };
            AssetDatabase.CreateAsset(cleanLayer, layerPath);
        }

        cleanLayer.diffuseTexture = cleanTexture;
        cleanLayer.normalMapTexture = null;
        cleanLayer.maskMapTexture = null;
        cleanLayer.tileSize = new Vector2(Mathf.Max(1f, terrainData.size.x), Mathf.Max(1f, terrainData.size.z));
        cleanLayer.tileOffset = Vector2.zero;
        cleanLayer.specular = Color.black;
        cleanLayer.metallic = 0f;
        cleanLayer.smoothness = 0f;
        EditorUtility.SetDirty(cleanLayer);

        Undo.RecordObject(terrainData, "Reset Ground Terrain Surface Layers");
        terrainData.terrainLayers = new[] { cleanLayer };

        int width = Mathf.Max(1, terrainData.alphamapWidth);
        int height = Mathf.Max(1, terrainData.alphamapHeight);
        float[,,] alphamaps = new float[height, width, 1];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                alphamaps[y, x, 0] = 1f;
        }
        terrainData.SetAlphamaps(0, 0, alphamaps);
        terrainData.SetBaseMapDirty();

        EditorUtility.SetDirty(cleanTexture);
        EditorUtility.SetDirty(cleanLayer);
        EditorUtility.SetDirty(terrainData);
        AssetDatabase.SaveAssets();
    }

    private static string GetTerrainDataFolder(MapDefinition map)
    {
        string mapPath = map != null ? AssetDatabase.GetAssetPath(map).Replace("\\", "/") : "";
        string mapFolder = string.IsNullOrWhiteSpace(mapPath)
            ? SkyPrisonMapEditorPage.DefaultMapCreateFolder
            : Path.GetDirectoryName(mapPath)?.Replace("\\", "/");

        if (string.IsNullOrWhiteSpace(mapFolder))
            mapFolder = SkyPrisonMapEditorPage.DefaultMapCreateFolder;

        return $"{mapFolder}/{GroundTerrainDataFolderName}";
    }

    private static BaseGroundBlock EnsureBaseGroundBlockToMap(MapDefinition map, Transform worldRoot)
    {
        if (worldRoot == null)
            return null;

        GameObject groundRoot = FindOrCreateChildObject(worldRoot, "GroundRoot");
        GameObject groundBlockObject = FindOrCreateChildObject(groundRoot.transform, "GroundBlock_01");
        GameObject groundVisual = FindOrCreateChildObject(groundBlockObject.transform, "GroundVisual");
        GameObject groundPhysics = FindOrCreateChildObject(groundBlockObject.transform, GroundPhysicsObjectName);
        GameObject groundCollider = FindOrCreateChildObject(groundBlockObject.transform, "GroundCollider");
        GameObject groundDebug = FindOrCreateChildObject(groundBlockObject.transform, "GroundDebug");

        // GroundBlock 是实际渲染到 Game 里的世界地面，默认必须进入 World3D 层，
        // 否则 Main Camera 的 Culling Mask 可能看不到它。补齐 / 同步结构时统一矫正。
        ApplyWorld3DLayerToObject(groundBlockObject);

        Vector3 center = map != null ? map.mapBoundsCenter : Vector3.zero;
        Vector3 size = map != null ? map.mapBoundsSize : new Vector3(64f, 6f, 64f);
        size.x = Mathf.Max(1f, Mathf.Abs(size.x));
        size.y = Mathf.Max(0.1f, Mathf.Abs(size.y));
        size.z = Mathf.Max(1f, Mathf.Abs(size.z));

        // GroundBlock_01 本身就是“地图地面数据域”的可视尺寸对象。
        // 之前只缩放 GroundVisual 子节点，导致在层级里选中 GroundBlock_01 时 Transform 仍然是 1/1/1，
        // 看起来像没有同步到 MapBounds。这里改成：父节点 X/Z 直接等于地图边界尺寸，
        // 子节点只负责显示 / 碰撞的单位形态，避免语义混乱。
        Undo.RecordObject(groundBlockObject.transform, "Sync GroundBlock To Map Bounds");
        groundBlockObject.transform.position = center;
        groundBlockObject.transform.rotation = Quaternion.identity;
        groundBlockObject.transform.localScale = new Vector3(size.x, 1f, size.z);

        BaseGroundBlock block = groundBlockObject.GetComponent<BaseGroundBlock>();
        if (block == null)
            block = groundBlockObject.AddComponent<BaseGroundBlock>();

        if (block == null)
        {
            Debug.LogError(
                "[SkyPrisonMapEditorUtility] 无法给 GroundBlock_01 添加 BaseGroundBlock。" +
                "请确认 BaseGroundBlock.cs 位于非 Editor 目录，例如 Assets/_Project/Scripts/Core/Map/Ground/。",
                groundBlockObject);
            return null;
        }

        block.mapBoundsCenter = center;
        block.mapBoundsSize = size;
        block.defaultGroundHeight = center.y;
        block.groundVisualRoot = groundVisual.transform;
        block.groundColliderRoot = groundCollider.transform;
        block.groundDebugRoot = groundDebug.transform;

        GroundSurfaceMarker marker = groundBlockObject.GetComponent<GroundSurfaceMarker>();
        if (marker == null)
            marker = groundBlockObject.AddComponent<GroundSurfaceMarker>();

        if (marker == null)
        {
            Debug.LogError(
                "[SkyPrisonMapEditorUtility] 无法给 GroundBlock_01 添加 GroundSurfaceMarker。" +
                "请确认 GroundSurfaceMarker.cs / GroundSurfaceType.cs 位于非 Editor 目录，例如 Assets/_Project/Scripts/Core/Map/Ground/。",
                groundBlockObject);
            return block;
        }

        marker.surfaceType = block.defaultSurfaceType;
        marker.isBaseGround = true;

        SetupGroundVisual(groundVisual);
        SetupGroundPhysics(groundPhysics, size);
        SetupGroundCollider(groundCollider, size);

        groundDebug.transform.localPosition = Vector3.zero;
        groundDebug.transform.localRotation = Quaternion.identity;
        groundDebug.transform.localScale = Vector3.one;

        EditorUtility.SetDirty(block);
        EditorUtility.SetDirty(marker);
        EditorUtility.SetDirty(groundBlockObject);
        return block;
    }

    private static void SetupGroundVisual(GameObject groundVisual)
    {
        if (groundVisual == null)
            return;

        ApplyWorld3DLayerToObject(groundVisual);

        Undo.RecordObject(groundVisual.transform, "Sync GroundVisual To GroundBlock");
        groundVisual.transform.localPosition = Vector3.zero;
        groundVisual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // 父节点 GroundBlock_01 已经按 MapBounds 缩放；子 Quad 保持单位尺寸即可。
        groundVisual.transform.localScale = Vector3.one;

        MeshFilter meshFilter = groundVisual.GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = groundVisual.AddComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        MeshRenderer renderer = groundVisual.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = groundVisual.AddComponent<MeshRenderer>();
        Material groundMat = FindOrCreateGroundMaterial();
        if (groundMat != null)
            renderer.sharedMaterial = groundMat;

        Collider collider = groundVisual.GetComponent<Collider>();
        if (collider != null)
            Object.DestroyImmediate(collider);

        EditorUtility.SetDirty(groundVisual);
    }

    private static void SetupGroundPhysics(GameObject groundPhysics, Vector3 size)
    {
        if (groundPhysics == null)
            return;

        ApplyGroundPhysicsLayerToObject(groundPhysics);

        Undo.RecordObject(groundPhysics.transform, "Sync GroundPhysics To GroundBlock");
        groundPhysics.transform.localPosition = Vector3.zero;
        groundPhysics.transform.localRotation = Quaternion.identity;
        groundPhysics.transform.localScale = Vector3.one;

        BoxCollider box = groundPhysics.GetComponent<BoxCollider>();
        if (box == null)
            box = groundPhysics.AddComponent<BoxCollider>();

        Undo.RecordObject(box, "Sync GroundPhysics Size");

        // GroundPhysics 的职责是提供“真实地平线”。
        // GroundBlock_01 / GroundVisual 的 local Y = 0 代表地面上表面，
        // 所以物理盒不能以 center=0 上下各长一半，否则顶面会被抬高 size.y / 2。
        // 正确做法：上顶贴住父节点 / Visual 的地面高度，厚度只向下延伸。
        float groundThickness = Mathf.Max(0.1f, size.y);
        box.center = new Vector3(0f, -groundThickness * 0.5f, 0f);
        // X/Z 由父节点 GroundBlock_01 的 Transform Scale 提供；GroundPhysics 自身只保持单位盒。
        box.size = new Vector3(1f, groundThickness, 1f);
        box.isTrigger = false;

        EditorUtility.SetDirty(groundPhysics);
        EditorUtility.SetDirty(box);
    }

    private static void SetupGroundCollider(GameObject groundCollider, Vector3 size)
    {
        if (groundCollider == null)
            return;

        ApplyWorld3DLayerToObject(groundCollider);

        Undo.RecordObject(groundCollider.transform, "Sync GroundCollider To GroundBlock");
        groundCollider.transform.localPosition = Vector3.zero;
        groundCollider.transform.localRotation = Quaternion.identity;
        groundCollider.transform.localScale = Vector3.one;

        BoxCollider box = groundCollider.GetComponent<BoxCollider>();
        if (box == null)
            box = groundCollider.AddComponent<BoxCollider>();

        Undo.RecordObject(box, "Sync GroundCollider Size");

        // GroundCollider 与 GroundPhysics 共用同一条地平线规则：
        // 顶面贴住 GroundBlock_01 / GroundVisual，厚度向下延伸。
        float groundThickness = Mathf.Max(0.1f, size.y);
        box.center = new Vector3(0f, -groundThickness * 0.5f, 0f);
        // X/Z 由父节点 GroundBlock_01 的 Transform Scale 提供，Collider 本体保持单位宽深；
        // Y 不走父节点缩放，所以这里保留地图边界厚度。
        box.size = new Vector3(1f, groundThickness, 1f);
        box.isTrigger = false;
        EditorUtility.SetDirty(groundCollider);
    }

    private static GameObject FindOrCreateRootObject(string name, Scene scene)
    {
        GameObject obj = GameObject.Find(name);
        if (obj != null)
            return obj;

        obj = new GameObject(name);
        if (scene.IsValid())
            EditorSceneManager.MoveGameObjectToScene(obj, scene);
        return obj;
    }

    private static GameObject FindOrCreateChildObject(Transform parent, string name)
    {
        if (parent == null)
            return new GameObject(name);

        Transform found = parent.Find(name);
        if (found != null)
            return found.gameObject;

        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        return obj;
    }

    public static void EnsureFogOfWarLayerAndGamePlayCamera()
    {
        int fogLayer = EnsureLayerExistsAtIndex("FogOfWar", 20);
        if (fogLayer < 0)
            return;

        GameObject fogOverlay = GameObject.Find("FogOverlay");
        if (fogOverlay != null)
            ApplyFogOfWarLayerToObject(fogOverlay);

        Camera gamePlayCamera = FindCameraByName("GamePlayCamera");
        if (gamePlayCamera != null)
        {
            Undo.RecordObject(gamePlayCamera, "Add FogOfWar To GamePlayCamera Culling Mask");
            gamePlayCamera.cullingMask |= 1 << fogLayer;
            EditorUtility.SetDirty(gamePlayCamera);
        }
    }

    private static void ApplyFogOfWarLayerToObject(GameObject obj)
    {
        if (obj == null)
            return;

        int fogLayer = EnsureLayerExistsAtIndex("FogOfWar", 20);
        if (fogLayer < 0)
            return;

        obj.layer = fogLayer;
        Transform[] children = obj.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null)
                children[i].gameObject.layer = fogLayer;
        }
    }

    private static void ApplyWorld3DLayerToObject(GameObject obj)
    {
        if (obj == null)
            return;

        int world3DLayer = EnsureLayerNameAtIndex(World3DLayerName, World3DLayerIndex);
        if (world3DLayer < 0)
        {
            Debug.LogWarning($"[SkyPrisonMapEditorUtility] Layer '{World3DLayerName}' not found. Ground terrain layer was not changed.", obj);
            return;
        }

        Transform[] children = obj.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == null)
                continue;

            // GroundPhysics 是独立物理层，必须保持 21:GroundPhysics；
            // 同步 GroundBlock / GroundVisual 时不能被递归改成 World3D。
            if (IsGroundPhysicsTransform(children[i]))
                continue;

            Undo.RecordObject(children[i].gameObject, "Set GroundBlock Layer To World3D");
            children[i].gameObject.layer = world3DLayer;
            EditorUtility.SetDirty(children[i].gameObject);
        }
    }

    private static void ApplyGroundPhysicsLayerToObject(GameObject obj)
    {
        if (obj == null)
            return;

        int groundPhysicsLayer = EnsureLayerExistsAtIndex(GroundPhysicsLayerName, GroundPhysicsLayerIndex);
        if (groundPhysicsLayer < 0)
        {
            Debug.LogWarning("[SkyPrisonMapEditorUtility] Layer 'GroundPhysics' not found. GroundPhysics layer was not changed.", obj);
            return;
        }

        Transform[] children = obj.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] == null)
                continue;

            Undo.RecordObject(children[i].gameObject, "Set GroundPhysics Layer");
            children[i].gameObject.layer = groundPhysicsLayer;
            EditorUtility.SetDirty(children[i].gameObject);
        }
    }

    private static bool IsGroundPhysicsTransform(Transform transform)
    {
        while (transform != null)
        {
            if (transform.name == GroundPhysicsObjectName)
                return true;

            transform = transform.parent;
        }

        return false;
    }

    private static Camera FindCameraByName(string cameraName)
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].gameObject.name == cameraName)
                return cameras[i];
        }
        return null;
    }

    private static int EnsureLayerNameAtIndex(string layerName, int targetIndex)
    {
        Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
            return LayerMask.NameToLayer(layerName);

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || targetIndex < 0 || targetIndex >= layers.arraySize)
            return LayerMask.NameToLayer(layerName);

        bool changed = false;
        for (int i = 0; i < layers.arraySize; i++)
        {
            if (i == targetIndex)
                continue;

            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (layer != null && layer.stringValue == layerName)
            {
                layer.stringValue = "";
                changed = true;
            }
        }

        SerializedProperty target = layers.GetArrayElementAtIndex(targetIndex);
        if (target != null && target.stringValue != layerName)
        {
            target.stringValue = layerName;
            changed = true;
        }

        if (changed)
        {
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        return targetIndex;
    }

    private static int EnsureLayerExistsAtIndex(string layerName, int preferredIndex)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
            return existing;

        Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
        {
            Debug.LogWarning("[SkyPrisonMapEditorUtility] TagManager.asset not found. Cannot create FogOfWar layer.");
            return -1;
        }

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null || preferredIndex < 0 || preferredIndex >= layers.arraySize)
            return -1;

        SerializedProperty targetLayer = layers.GetArrayElementAtIndex(preferredIndex);
        if (targetLayer != null && string.IsNullOrWhiteSpace(targetLayer.stringValue))
        {
            targetLayer.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            return preferredIndex;
        }

        Debug.LogWarning($"[SkyPrisonMapEditorUtility] Layer {preferredIndex} is already occupied by '{targetLayer?.stringValue}'. Please set layer {preferredIndex} to '{layerName}'.");
        return LayerMask.NameToLayer(layerName);
    }

    public static void SyncMapBoundsToScene(MapDefinition map)
    {
        if (map == null)
            return;

        SkyPrisonMapBounds bounds = Object.FindFirstObjectByType<SkyPrisonMapBounds>();
        if (bounds != null)
        {
            bounds.sourceMode = SkyPrisonMapBounds.BoundsSourceMode.Manual;
            bounds.center = map.mapBoundsCenter;
            bounds.size = map.mapBoundsSize;
            bounds.RefreshBounds();
            EditorUtility.SetDirty(bounds);
        }

        SyncFogOverlayToMap(map);
        SyncGroundTerrainToMapBounds(map);
        EnsureFogOfWarLayerAndGamePlayCamera();

        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void SyncFogOverlayToMap(MapDefinition map)
    {
        if (map == null)
            return;

        GameObject fogOverlay = GameObject.Find("FogOverlay");
        if (fogOverlay == null)
        {
            GameObject visionSystem = GameObject.Find("VisionSystem");
            if (visionSystem != null)
                fogOverlay = FindOrCreateChildObject(visionSystem.transform, "FogOverlay");
        }

        if (fogOverlay == null)
            return;

        ApplyFogOfWarLayerToObject(fogOverlay);

        fogOverlay.transform.position = new Vector3(
            map.mapBoundsCenter.x,
            map.mapBoundsCenter.y + 0.08f,
            map.mapBoundsCenter.z);

        fogOverlay.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        fogOverlay.transform.localScale = new Vector3(
            Mathf.Max(1f, map.mapBoundsSize.x),
            Mathf.Max(1f, map.mapBoundsSize.z),
            1f);

        MeshFilter meshFilter = fogOverlay.GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = fogOverlay.AddComponent<MeshFilter>();
        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        MeshRenderer overlayRenderer = fogOverlay.GetComponent<MeshRenderer>();
        if (overlayRenderer == null)
            overlayRenderer = fogOverlay.AddComponent<MeshRenderer>();

        Material fogMat = FindOrCreateFogMaterial();
        if (fogMat != null)
            overlayRenderer.sharedMaterial = fogMat;

        Collider overlayCollider = fogOverlay.GetComponent<Collider>();
        if (overlayCollider != null)
            Object.DestroyImmediate(overlayCollider);

        SkyPrisonFogMaskRenderer fogMaskRenderer = fogOverlay.GetComponent<SkyPrisonFogMaskRenderer>();
        if (fogMaskRenderer == null)
            fogMaskRenderer = fogOverlay.AddComponent<SkyPrisonFogMaskRenderer>();

        fogMaskRenderer.ResolveReferences();
        EditorUtility.SetDirty(fogOverlay);
        EditorUtility.SetDirty(fogMaskRenderer);
    }

    public static void PullSceneBoundsToMap(MapDefinition map)
    {
        if (map == null)
            return;

        SkyPrisonMapBounds bounds = Object.FindFirstObjectByType<SkyPrisonMapBounds>();
        if (bounds == null)
            return;

        bounds.RefreshBounds();
        map.mapBoundsCenter = bounds.ResolvedBounds.center;
        map.mapBoundsSize = bounds.ResolvedBounds.size;
        EditorUtility.SetDirty(map);
    }

    /// <summary>
    /// 将 WorldRoot 下的地图内容整体平移到 MapBounds 中心。
    /// 只移动 WorldRoot，不缩放、不旋转、不改 System / UnitRoot / Camera / Canvas 等系统节点。
    /// </summary>
    public static bool MoveWorldRootToMapBoundsCenter()
    {
        return MoveMapContentRootsToMapBoundsCenter(false);
    }

    /// <summary>
    /// 将 WorldRoot 下的地图内容平移到 MapBounds 中心，同时让 UnitRoot 跟随同一段偏移。
    /// 适合地图内容与单位配置、出生点、孵化器已经相对摆好，但整体偏离 MapBounds 的情况。
    /// </summary>
    public static bool MoveWorldRootAndUnitRootToMapBoundsCenter()
    {
        return MoveMapContentRootsToMapBoundsCenter(true);
    }

    private static bool MoveMapContentRootsToMapBoundsCenter(bool includeUnitRoot)
    {
        SkyPrisonMapBounds bounds = Object.FindFirstObjectByType<SkyPrisonMapBounds>();
        if (bounds == null)
        {
            EditorUtility.DisplayDialog("未找到 MapBounds", "当前 Scene 中没有找到 SkyPrisonMapBounds。", "确定");
            return false;
        }

        GameObject worldRoot = GameObject.Find("WorldRoot");
        if (worldRoot == null)
        {
            EditorUtility.DisplayDialog("未找到 WorldRoot", "当前 Scene 中没有找到 WorldRoot。请先校对并补齐基础节点。", "确定");
            return false;
        }

        if (!TryCalculateContentBounds(worldRoot.transform, out Bounds contentBounds))
        {
            EditorUtility.DisplayDialog("没有可校正的地图内容", "WorldRoot 下没有找到 Renderer 或 Collider，无法计算内容中心。", "确定");
            return false;
        }

        bounds.RefreshBounds();
        Vector3 targetCenter = bounds.ResolvedBounds.center;
        Vector3 currentCenter = contentBounds.center;
        Vector3 offset = targetCenter - currentCenter;

        // 只做 XZ 平面平移。Y 保持不变，避免破坏 2.5D 高度关系。
        offset.y = 0f;

        if (offset.sqrMagnitude < 0.000001f)
        {
            EditorUtility.DisplayDialog("无需移动", includeUnitRoot
                ? "WorldRoot 内容已经基本位于 MapBounds 中心，UnitRoot 不需要跟随移动。"
                : "WorldRoot 内容已经基本位于 MapBounds 中心。", "确定");
            return false;
        }

        List<Transform> rootsToMove = new List<Transform>();
        rootsToMove.Add(worldRoot.transform);

        GameObject unitRoot = GameObject.Find("UnitRoot");
        if (includeUnitRoot && unitRoot != null)
            rootsToMove.Add(unitRoot.transform);

        Undo.RecordObjects(rootsToMove.ToArray(), includeUnitRoot
            ? "Move WorldRoot And UnitRoot To MapBounds Center"
            : "Move WorldRoot To MapBounds Center");

        for (int i = 0; i < rootsToMove.Count; i++)
        {
            Transform root = rootsToMove[i];
            if (root == null)
                continue;

            root.position += offset;
            EditorUtility.SetDirty(root);
        }

        Scene scene = worldRoot.scene;
        if (scene.IsValid())
            EditorSceneManager.MarkSceneDirty(scene);

        if (includeUnitRoot && unitRoot == null)
        {
            EditorUtility.DisplayDialog("已移动地图内容", "已将 WorldRoot 居中到 MapBounds。\n当前 Scene 没有找到 UnitRoot，所以没有移动单位配置。", "确定");
        }

        return true;
    }

    private static bool TryCalculateContentBounds(Transform root, out Bounds bounds)
    {
        bounds = new Bounds();
        if (root == null)
            return false;

        bool hasBounds = false;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        Collider2D[] colliders2D = root.GetComponentsInChildren<Collider2D>(true);
        for (int i = 0; i < colliders2D.Length; i++)
        {
            Collider2D collider = colliders2D[i];
            if (collider == null)
                continue;

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }


    public static Material FindOrCreateGroundMaterial()
    {
        const string materialPath = "Assets/_Project/Materials/Map/MAT_BaseGroundBlock_Default.mat";

        Shader shader = Shader.Find("SkyPrison/Map/BaseGroundBlockMasked");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat != null)
        {
            if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
                EditorUtility.SetDirty(mat);
                AssetDatabase.SaveAssets();
            }

            ConfigureGroundMaterialDefaults(mat);
            return mat;
        }

        EnsureFolderExists("Assets/_Project/Materials/Map");

        mat = new Material(shader);
        mat.name = "MAT_BaseGroundBlock_Default";
        ConfigureGroundMaterialDefaults(mat);

        AssetDatabase.CreateAsset(mat, materialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static void ConfigureGroundMaterialDefaults(Material mat)
    {
        if (mat == null)
            return;

        Color baseColor = new Color(0.34f, 0.34f, 0.34f, 1f);

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", baseColor);
        if (mat.HasProperty("_MaskThreshold"))
            mat.SetFloat("_MaskThreshold", 0.01f);

        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
    }

    public static Material FindOrCreateFogMaterial()
    {
        const string materialPath = "Assets/_Project/Shaders/Custom/SkyPrisonFogOfWarOverlay.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (mat != null)
            return mat;

        Shader shader = Shader.Find("SkyPrison/FogOfWarOverlay");
        if (shader == null)
            return null;

        EnsureFolderExists("Assets/_Project/Shaders/Custom");
        mat = new Material(shader);
        AssetDatabase.CreateAsset(mat, materialPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return mat;
    }

    public static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
