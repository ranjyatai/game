using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 出发转场：把当前整屏画面截下来，四周向中心凹陷（和暂停菜单同一类畸变，强度放大）、边缘变糊、色差拉开，
/// 最后整个吸向中心变成黑屏。截屏贴在一个挂在 DontDestroyOnLoad 上的最顶层画布里，
/// 所以后面切场景时它还盖着，等 LoadingScene 接手后自己消失。全程用不缩放时间（选关界面期间世界是冻住的）。
/// </summary>
public static class SkyPrisonDepartWarp
{
    const float DentSeconds = 0.55f;       // 凹陷从 0 长到最深
    const float CollapseSeconds = 0.4f;    // 吸入中心 + 压黑
    const float BlackHold = 0.12f;         // 黑屏停半拍，接读条

    public static IEnumerator Play()
    {
        yield return new WaitForEndOfFrame();
        Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
        var sh = Resources.Load<Shader>("Shaders/UIDepartWarp");
        if (shot == null || sh == null) { if (shot != null) Object.Destroy(shot); yield break; }
        shot.wrapMode = TextureWrapMode.Clamp;

        var go = new GameObject("[DepartWarp]", typeof(RectTransform));
        Object.DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31500;
        var img = new GameObject("Shot", typeof(RectTransform), typeof(RawImage)); img.transform.SetParent(go.transform, false);
        var rt = (RectTransform)img.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        var raw = img.GetComponent<RawImage>(); raw.texture = shot; raw.raycastTarget = true;   // 盖住整屏，转场期间点不到后面的界面
        var mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        raw.material = mat;
        mat.SetFloat("_Strength", 0f); mat.SetFloat("_Collapse", 0f);

        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.DepartWarp);
        float t = 0f;
        while (t < DentSeconds)
        {
            t += Time.unscaledDeltaTime; float p = Mathf.Clamp01(t / DentSeconds);
            mat.SetFloat("_Strength", p * p * (3f - 2f * p));   // 慢起慢停，凹陷是「长」出来的
            yield return null;
        }
        mat.SetFloat("_Strength", 1f);
        t = 0f;
        while (t < CollapseSeconds)
        {
            t += Time.unscaledDeltaTime; float p = Mathf.Clamp01(t / CollapseSeconds);
            mat.SetFloat("_Collapse", p * p);   // 加速吸入
            yield return null;
        }
        mat.SetFloat("_Collapse", 1f);
        var holder = go.AddComponent<Cleaner>(); holder.shot = shot; holder.mat = mat;
        yield return new WaitForSecondsRealtime(BlackHold);
    }

    /// <summary>黑屏撑到场景切换发生，再多留一小会儿后清掉自己（并释放截屏和材质）。</summary>
    sealed class Cleaner : MonoBehaviour
    {
        public Texture2D shot; public Material mat; float t0;
        void Start() { t0 = Time.realtimeSinceStartup; }
        void Update() { if (Time.realtimeSinceStartup - t0 > 0.6f) Destroy(gameObject); }
        void OnDestroy() { if (shot != null) Destroy(shot); if (mat != null) Destroy(mat); }
    }
}
