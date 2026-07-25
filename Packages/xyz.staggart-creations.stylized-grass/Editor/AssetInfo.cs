// Stylized Grass Shader © Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//
// ⚠️ WARNING: UNAUTHORIZED USE OR DISTRIBUTION IS STRICTLY PROHIBITED
// • Copying, referencing, or reverse-engineering this source code for the creation of new Asset Store or derivative products,
//   or any other publicly distributed content is strictly forbidden and will result in legal action.
// • Studying this file for the purpose of reproducing its functionality in your own assets or tools is not permitted.
// • If you are viewing this file as a reference, please close it immediately to avoid unintentional design influence or potential EULA violations.
// • Uploading this file or any derivative of it to a public GitHub or similar repository will trigger an automated DMCA takedown request.
// • Studying to understand for personal, educational or integration purposes is allowed, studying to reproduce is not.

using System;
using System.Collections.Generic;
using System.Net;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

#if !UNITY_6000_0_OR_NEWER
#error [Stylized Grass v2] Imported in a version older than Unity 6, all present script and shader compile errors are valid and not something to simply be fixed. Upgrade the project to Unity 6 to resolve them.
#endif

namespace sc.stylizedgrass.editor
{
    public static class AssetInfo
    {
        public const string ASSET_NAME = "Stylized Grass Shader";
        public const string ASSET_ID = "357954";
        public const string ASSET_ABRV = "SGS";

        public const string INSTALLED_VERSION = "2.0.1";
        
        private static readonly Dictionary<string, int> REQUIRED_PATCH_VERSIONS = new Dictionary<string, int>()
        {
            { "6000.0", 22 },
            { "6000.1", 3 }
        };
        
        public const string MIN_UNITY_VERSION = "6.3";

        public const string DOC_URL = "https://staggart.xyz/support/documentation/sgs-unity-6-docs/";
        public const string FORUM_URL = "https://forum.unity.com/threads/804000/";
        public const string DISCORD_INVITE_URL = "https://staggart.xyz/support/discord";

        public const string URP_PACKAGE_ID = "com.unity.render-pipelines.universal";

        public static PackageInfo GetURPPackage()
        {
            SearchRequest request = Client.Search(URP_PACKAGE_ID);
                
            while (request.Status == StatusCode.InProgress) { /* Waiting... */ }

            if (request.Status == StatusCode.Failure)
            {
                Debug.LogError("Failed to retrieve URP package from Package Manager...");
                return null;
            }

            return request.Result[0];
        }
        
        public static void OpenInPackageManager()
        {
            Application.OpenURL("com.unity3d.kharma:content/" + ASSET_ID);
        }

        public static void OpenForumPage()
        {
            Application.OpenURL(FORUM_URL);
        }
        
        public static void OpenAssetStore(string url = null)
        {
            if (url == string.Empty) url = "https://assetstore.unity.com/packages/slug/" + ASSET_ID;
            
            Application.OpenURL(url + "?aid=1011l7Uk8&pubref=sgseditor");
        }
        
        public static void OpenReviewsPage()
        {
            Application.OpenURL($"https://assetstore.unity.com/packages/slug/{ASSET_ID}?aid=1011l7Uk8&pubref=sgseditor#reviews");
        }
        
        //Sorry, as much as I hate to intrude on an entire project, this is the only way in Unity to track importing or updating an asset
        public class ImportOrUpdateAsset : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromPath)
            {
                for (int i = 0; i < importedAssets.Length; i++)
                {
                    OnPreProcessAsset(importedAssets[i]);
                }
            }
            
            private static void OnPreProcessAsset(string m_assetPath)
            {
                //Importing/updating the asset
                if (m_assetPath.EndsWith("xyz.staggart-creations.stylized-grass/Editor/AssetInfo.cs"))
                {
                    Installer.Initialize();
                    Installer.OpenWindowIfNeeded();
                }
            }
        }
        
        public static class VersionChecking
        {
            public static string MinRequiredUnityVersion = "undefined";

            public enum UnityVersionType
            {
                Release,
                Beta,
                Alpha
            }
            public static UnityVersionType unityVersionType;
            
            [InitializeOnLoadMethod]
            static void Initialize()
            {
                if (CHECK_PERFORMED == false)
                {
                    CheckForUpdate(false);
                    CHECK_PERFORMED = true;
                }
            }
            
            private static bool CHECK_PERFORMED
            {
                get => SessionState.GetBool(ASSET_ABRV + "_VERSION_CHECK_PERFORMED", false);
                set => SessionState.SetBool(ASSET_ABRV + "_VERSION_CHECK_PERFORMED", value);
            }
            
            public static string LATEST_VERSION
            {
                get => SessionState.GetString(ASSET_ABRV + "_LATEST_VERSION", INSTALLED_VERSION);
                set => SessionState.SetString(ASSET_ABRV + "_LATEST_VERSION", value);
            }
            public static bool UPDATE_AVAILABLE
            {
                get => SessionState.GetBool(ASSET_ABRV + "UPDATE_AVAILABLE", false);
                set => SessionState.SetBool(ASSET_ABRV + "_UPDATE_AVAILABLE", value);
            }

            public static string GetUnityVersion()
            {
                string version = UnityEditorInternal.InternalEditorUtility.GetFullUnityVersion();
                
                //Remove GUID in parenthesis 
                return version.Substring(0, version.LastIndexOf(" ("));
            }
            
            public static bool supportedMajorVersion = true;
            public static bool supportedPatchVersion = true;
            public static bool compatibleVersion = true;
            
            private static void ParseUnityVersion(string versionString, out int major, out int minor, out int patch, out UnityVersionType type)
            {
                var match = System.Text.RegularExpressions.Regex.Match(versionString, @"^(\d+)\.(\d+)\.(\d+)");
                if (match.Success)
                {
                    major = int.Parse(match.Groups[1].Value);
                    minor = int.Parse(match.Groups[2].Value);
                    patch = int.Parse(match.Groups[3].Value);
                    
                    if (versionString.Contains("b"))
                    {
                        type = UnityVersionType.Beta;
                    }
                    else if (versionString.Contains("a"))
                    {
                        type = UnityVersionType.Alpha;
                    }
                    else
                    {
                        type = UnityVersionType.Release;
                    }
                }
                else
                {
                    throw new FormatException($"Invalid Unity version format: {versionString}.");
                }
            }
            
            public static void CheckUnityVersion()
            {
                string unityVersion = GetUnityVersion();
                
                #if !UNITY_6000_0_OR_NEWER
                compatibleVersion = false;
                #endif
                
                #if !UNITY_6000_0_OR_NEWER || UNITY_7000_OR_NEWER
                supportedMajorVersion = false;
                #endif
                
                ParseUnityVersion(unityVersion, out int major, out int minor, out int patch, out unityVersionType);

                //Get the minimum required patch version for the current Unity version (eg. 6000.1)
                if (REQUIRED_PATCH_VERSIONS.TryGetValue($"{major}.{minor}", out int minPatchVersion))
                {
                    supportedPatchVersion = patch >= minPatchVersion;
                }
                else
                {
                    //None found, current Unity version likely unknown
                    supportedPatchVersion = true;
                }

                MinRequiredUnityVersion = $"{major}.{minor}.{minPatchVersion}";
            }

            public static string apiResult;
            private static bool showPopup;

            public enum VersionStatus
            {
                UpToDate,
                Outdated
            }

            public enum QueryStatus
            {
                Fetching,
                Completed,
                Failed
            }
            public static QueryStatus queryStatus = QueryStatus.Completed;

#if SGS_DEV
            [MenuItem("SGS/Check for update")]
#endif
            public static void GetLatestVersionPopup()
            {
                CheckForUpdate(true);
            }

            public static void CheckForUpdate(bool showPopup = false)
            {
                //UPDATE_AVAILABLE = true; return;
                
                //Offline
                if (Application.internetReachability == NetworkReachability.NotReachable)
                {
                    UPDATE_AVAILABLE = false;
                    return;
                }
                
                VersionChecking.showPopup = showPopup;

                queryStatus = QueryStatus.Fetching;

                var url = $"https://api.assetstore.unity3d.com/package/latest-version/{ASSET_ID}";

                using (System.Net.WebClient webClient = new System.Net.WebClient())
                {
                    webClient.DownloadStringCompleted += OnRetrievedAPIContent;
                    webClient.DownloadStringAsync(new System.Uri(url), apiResult);
                }
            }
            
            private class AssetStoreItem
            {
                public string name;
                public string version;
            }

            private static void OnRetrievedAPIContent(object sender, DownloadStringCompletedEventArgs e)
            {
                if (e.Error == null && !e.Cancelled)
                {
                    string result = e.Result;

                    AssetStoreItem asset = (AssetStoreItem)JsonUtility.FromJson(result, typeof(AssetStoreItem));

                    LATEST_VERSION = asset.version;
                    //LATEST_VERSION = "9.9.9";
                    
                    Version remoteVersion = new Version(asset.version);
                    Version installedVersion = new Version(AssetInfo.INSTALLED_VERSION);

                    UPDATE_AVAILABLE = remoteVersion > installedVersion;
                    
#if SGS_DEV
                    Debug.Log("<b>Asset store API</b> Update available = " + UPDATE_AVAILABLE + " (Installed:" + INSTALLED_VERSION + ") (Remote:" + LATEST_VERSION + ")");
#endif

                    queryStatus = QueryStatus.Completed;

                    if (VersionChecking.showPopup)
                    {
                        if (UPDATE_AVAILABLE)
                        {
                            if (EditorUtility.DisplayDialog(ASSET_NAME + ", version " + INSTALLED_VERSION, "An updated version is available: " + LATEST_VERSION, "Open Package Manager", "Close"))
                            {
                                OpenInPackageManager();
                            }
                        }
                        else
                        {
                            if (EditorUtility.DisplayDialog(ASSET_NAME + ", version " + INSTALLED_VERSION, "Installed version is up-to-date!", "Close")) { }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[" + ASSET_NAME + "] Contacting update server failed: " + e.Error.Message);
                    queryStatus = QueryStatus.Failed;
                }
            }
        }
    }
}