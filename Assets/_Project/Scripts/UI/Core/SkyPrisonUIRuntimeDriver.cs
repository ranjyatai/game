using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// Runtime owner for UI prefabs. Scenes do not need to contain HUD instances manually.
    /// Assign the HUD prefab once, then this driver instantiates and drives it at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkyPrisonUIRuntimeDriver_V1 : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private GameObject battleHudPrefab;
        [SerializeField] private bool spawnBattleHudOnAwake = true;
        [SerializeField] private bool dontDestroyRuntimeUI = true;

        [Header("Runtime Root")]
        [SerializeField] private Transform runtimeRoot;
        [SerializeField] private string runtimeRootName = "SkyPrisonRuntimeUIRoot";

        private GameObject battleHudInstance;
        private CanvasGroup battleHudCanvasGroup;
        private SkyPrisonPlayerHUDView_V4_StyleDriven playerHudView;

        [Header("Runtime HUD View Resolve")]
        [SerializeField] private string playerStatusAreaName = "PlayerStatusArea";
        [SerializeField] private bool preferPlayerStatusAreaDirectView = true;
        [SerializeField] private string resolvedPlayerHudViewPath = "Not resolved";
        [SerializeField] private bool resolvedPlayerHudViewIsDirectPlayerStatusArea;

        public GameObject BattleHudPrefab
        {
            get => battleHudPrefab;
            set => battleHudPrefab = value;
        }

        public GameObject BattleHudInstance => battleHudInstance;
        public SkyPrisonPlayerHUDView_V4_StyleDriven PlayerHudView => playerHudView;
        public bool IsBattleHudVisible => battleHudInstance != null && battleHudInstance.activeSelf && (battleHudCanvasGroup == null || battleHudCanvasGroup.alpha > 0f);

        public event System.Action<bool> OnBattleHudVisibilityChanged;

        private void Awake()
        {
            EnsureRuntimeRoot();

            if (spawnBattleHudOnAwake)
                SpawnBattleHUD();
        }

        public GameObject SpawnBattleHUD()
        {
            if (battleHudInstance != null)
                return battleHudInstance;

            if (battleHudPrefab == null)
            {
                Debug.LogWarning("[SkyPrison UI Driver] Battle HUD prefab is not assigned.", this);
                return null;
            }

            EnsureRuntimeRoot();
            battleHudInstance = Instantiate(battleHudPrefab, runtimeRoot);
            battleHudInstance.name = battleHudPrefab.name;

            battleHudCanvasGroup = battleHudInstance.GetComponent<CanvasGroup>();
            if (battleHudCanvasGroup == null)
                battleHudCanvasGroup = battleHudInstance.AddComponent<CanvasGroup>();

            ResolvePlayerHUDView(true);

            SkyPrisonUIPrefabMetadata_V1 metadata = battleHudInstance.GetComponent<SkyPrisonUIPrefabMetadata_V1>();
            SetBattleHUDVisible(metadata == null || metadata.defaultVisible, true);
            return battleHudInstance;
        }

        public void SetBattleHUDVisible(bool visible, bool instant = true)
        {
            if (battleHudInstance == null)
                SpawnBattleHUD();

            if (battleHudInstance == null)
                return;

            if (battleHudCanvasGroup == null)
                battleHudCanvasGroup = battleHudInstance.GetComponent<CanvasGroup>() ?? battleHudInstance.AddComponent<CanvasGroup>();

            battleHudCanvasGroup.alpha = visible ? 1f : 0f;
            battleHudCanvasGroup.interactable = visible;
            battleHudCanvasGroup.blocksRaycasts = visible;

            if (instant)
                battleHudInstance.SetActive(visible);

            OnBattleHudVisibilityChanged?.Invoke(visible);
        }

        public void UpdatePlayerPreviewValues(float hp, float maxHp, float lp, float maxLp, float normalizedLoad)
        {
            // V2:
            // Runtime data must be sent to the displayed PlayerStatusArea bridge view.
            // The module RT renderer may move the original visual tree to a hidden SourceRoot;
            // a plain GetComponentInChildren can accidentally cache that moved source view.
            ResolvePlayerHUDView(false);

            if (playerHudView == null)
                return;

            playerHudView.SetHp(hp, maxHp);
            playerHudView.SetLp(lp, maxLp);
            playerHudView.SetLoad(normalizedLoad);
        }

        private void ResolvePlayerHUDView(bool force)
        {
            if (battleHudInstance == null)
            {
                playerHudView = null;
                resolvedPlayerHudViewPath = "No HUD instance";
                resolvedPlayerHudViewIsDirectPlayerStatusArea = false;
                return;
            }

            if (!force && IsResolvedPlayerHUDViewStillValid(playerHudView))
                return;

            SkyPrisonPlayerHUDView_V4_StyleDriven resolved = null;
            bool directPlayerStatusArea = false;

            Transform playerStatusArea = FindTransformRecursive(battleHudInstance.transform, playerStatusAreaName);
            if (preferPlayerStatusAreaDirectView && playerStatusArea != null)
            {
                resolved = playerStatusArea.GetComponent<SkyPrisonPlayerHUDView_V4_StyleDriven>();
                directPlayerStatusArea = resolved != null;
            }

            if (resolved == null)
            {
                resolved = battleHudInstance.GetComponentInChildren<SkyPrisonPlayerHUDView_V4_StyleDriven>(true);
                directPlayerStatusArea = false;
            }

            playerHudView = resolved;
            resolvedPlayerHudViewIsDirectPlayerStatusArea = directPlayerStatusArea;
            resolvedPlayerHudViewPath = playerHudView != null
                ? GetTransformPath(playerHudView.transform, battleHudInstance.transform)
                : "Not found";
        }

        private bool IsResolvedPlayerHUDViewStillValid(SkyPrisonPlayerHUDView_V4_StyleDriven view)
        {
            if (view == null || battleHudInstance == null)
                return false;

            if (!view.transform.IsChildOf(battleHudInstance.transform))
                return false;

            if (!preferPlayerStatusAreaDirectView)
                return true;

            Transform playerStatusArea = FindTransformRecursive(battleHudInstance.transform, playerStatusAreaName);
            if (playerStatusArea == null)
                return true;

            SkyPrisonPlayerHUDView_V4_StyleDriven direct = playerStatusArea.GetComponent<SkyPrisonPlayerHUDView_V4_StyleDriven>();

            // If the bridge exists now, the cached view is valid only when it is that bridge.
            // If the bridge does not exist yet, keep the current fallback until the next update.
            return direct == null || direct == view;
        }

        private static Transform FindTransformRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrEmpty(targetName))
                return null;

            if (root.name == targetName)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindTransformRecursive(root.GetChild(i), targetName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static string GetTransformPath(Transform target, Transform root)
        {
            if (target == null)
                return string.Empty;

            if (root == null || target == root)
                return target.name;

            string path = target.name;
            Transform current = target.parent;
            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            if (current == root)
                path = root.name + "/" + path;

            return path;
        }

        private void EnsureRuntimeRoot()
        {
            if (runtimeRoot == null)
            {
                GameObject root = GameObject.Find(runtimeRootName);
                if (root == null)
                    root = new GameObject(runtimeRootName);

                runtimeRoot = root.transform;

                if (dontDestroyRuntimeUI && Application.isPlaying)
                    DontDestroyOnLoad(root);
            }

            // WindowManager 检查放在 early-return 之外，保证 runtimeRoot 已序列化时也能挂上
            if (runtimeRoot.GetComponent<SkyPrisonWindowManager_V1>() == null)
                runtimeRoot.gameObject.AddComponent<SkyPrisonWindowManager_V1>();
        }
    }
}
