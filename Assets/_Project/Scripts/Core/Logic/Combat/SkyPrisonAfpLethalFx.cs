using UnityEngine;

/// <summary>
/// 致命子弹命中时的表现：命中点一道很短的白色闪光 + 一圈冷绿冲击环(约 0.2 秒) + 一声比普通命中更脆的高音。
/// 全部运行时程序生成，不依赖美术资产。故意不用蓝火：蓝色在这个游戏里已经是电磁 / 冻结异常的颜色。
/// </summary>
public static class SkyPrisonAfpLethalFx
{
    static Sprite _glow, _ring;
    static AudioClip _clip;
    static Material _mat;
    static readonly Color Cold = new Color(0.42f, 0.92f, 0.68f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { _glow = null; _ring = null; _clip = null; _mat = null; }

    public static void Play(Vector3 pos)
    {
        Ensure();
        var go = new GameObject("[AfpLethalFx]") { hideFlags = HideFlags.HideAndDontSave };
        go.transform.position = pos;
        go.AddComponent<Fx>();
        SkyPrisonSystemSEPlayer.Play(SkyPrisonSystemSEType.AbilityLethalHit);
    }

    static void Ensure()
    {
        if (_mat == null)
        {
            var sh = Shader.Find("Sprites/Default");
            if (sh != null) _mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        }
        if (_glow == null) _glow = MakeSprite(false);
        if (_ring == null) _ring = MakeSprite(true);
        if (_clip == null) _clip = MakeClip();
    }

    static Sprite MakeSprite(bool ring)
    {
        const int S = 64;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S * 2f - 1f, v = (y + 0.5f) / S * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.82f) / 0.12f) : Mathf.Pow(Mathf.Clamp01(1f - d), 1.6f);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        tex.SetPixels32(px); tex.Apply();
        var sp = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), S);   // 1 个世界单位 = 整张图
        sp.hideFlags = HideFlags.HideAndDontSave;
        return sp;
    }

    static AudioClip MakeClip()
    {
        const int rate = 44100; int n = (int)(rate * 0.14f);
        var data = new float[n];
        var rnd = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = Mathf.Exp(-t * 38f);
            float tone = Mathf.Sin(2f * Mathf.PI * 2700f * t) * 0.55f + Mathf.Sin(2f * Mathf.PI * 4100f * t) * 0.30f;
            float click = (float)(rnd.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t * 160f) * 0.5f;
            data[i] = (tone * env + click) * 0.8f;
        }
        var clip = AudioClip.Create("AfpLethalCrack", n, 1, rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    sealed class Fx : MonoBehaviour
    {
        const float Life = 0.22f;
        SpriteRenderer _glowR, _ringR;
        float _t;

        void Awake()
        {
            _glowR = Child("Glow", _glow, 600);
            _ringR = Child("Ring", _ring, 601);
        }

        SpriteRenderer Child(string n, Sprite sp, int order)
        {
            var go = new GameObject(n) { hideFlags = HideFlags.HideAndDontSave };
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sp; sr.sortingOrder = order;
            if (_mat != null) sr.sharedMaterial = _mat;
            return sr;
        }

        void LateUpdate()
        {
            _t += Time.deltaTime;
            float p = Mathf.Clamp01(_t / Life);
            var cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;      // 始终朝向相机
            float gs = Mathf.Lerp(0.55f, 1.15f, 1f - (1f - p) * (1f - p));
            _glowR.transform.localScale = new Vector3(gs, gs, 1f);
            _glowR.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - p * 1.6f));
            float rs = Mathf.Lerp(0.25f, 1.25f, 1f - (1f - p) * (1f - p) * (1f - p));
            _ringR.transform.localScale = new Vector3(rs, rs, 1f);
            _ringR.color = new Color(Cold.r, Cold.g, Cold.b, 1f - p);
            if (p >= 1f) Destroy(gameObject);
        }
    }
}
