using UnityEngine;

namespace SkyPrison.Runtime.UI
{
    /// <summary>
    /// 运行时程序化生成圆角矩形 Sprite，可用于任何 Image 组件。
    /// 生成的 Sprite 以 9-slice 模式切割，可拉伸到任意尺寸而不变形圆角。
    /// </summary>
    public static class SkyPrisonRoundedRectSprite
    {
        // 缓存：避免相同参数重复生成纹理
        private static Texture2D _cachedTex;
        private static Sprite    _cachedSprite;
        private static int       _cachedRadius;
        private static int       _cachedTexSize;

        /// <summary>
        /// 获取圆角矩形 Sprite（有缓存则复用）。
        /// </summary>
        /// <param name="texSize">纹理边长（推荐 64~128，需 >= radius*2+2）</param>
        /// <param name="radius">圆角半径（像素）</param>
        public static Sprite Get(int texSize = 64, int radius = 12)
        {
            if (_cachedSprite != null && _cachedRadius == radius && _cachedTexSize == texSize)
                return _cachedSprite;

            _cachedTex    = GenerateTexture(texSize, radius);
            _cachedSprite = TextureToSprite(_cachedTex, radius);
            _cachedRadius = radius;
            _cachedTexSize = texSize;
            return _cachedSprite;
        }

        /// <summary>生成新的圆角矩形 Sprite（不缓存，用于需要不同参数的场合）。</summary>
        public static Sprite Create(int texSize = 64, int radius = 12)
        {
            Texture2D tex = GenerateTexture(texSize, radius);
            return TextureToSprite(tex, radius);
        }

        // ── 内部实现 ─────────────────────────────────────────────────

        private static Texture2D GenerateTexture(int size, int radius)
        {
            radius = Mathf.Clamp(radius, 0, size / 2);

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode   = TextureWrapMode.Clamp;

            Color[] pixels = new Color[size * size];
            Color white = Color.white;
            Color clear = Color.clear;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = IsInsideRoundedRect(x, y, size, size, radius)
                        ? white : clear;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        private static bool IsInsideRoundedRect(int px, int py, int w, int h, int r)
        {
            // 四个圆角中心坐标
            int x0 = r, x1 = w - 1 - r;
            int y0 = r, y1 = h - 1 - r;

            // 边缘之外直接裁掉
            if (px < 0 || px >= w || py < 0 || py >= h) return false;

            // 中间直条区域
            if (px >= x0 && px <= x1) return true;
            if (py >= y0 && py <= y1) return true;

            // 四个角落：检查是否在圆弧内
            int cx = (px < x0) ? x0 : x1;
            int cy = (py < y0) ? y0 : y1;
            float dx = px - cx, dy = py - cy;
            return dx * dx + dy * dy <= (float)r * r;
        }

        private static Sprite TextureToSprite(Texture2D tex, int radius)
        {
            int s = tex.width;
            // 9-slice border = radius，让圆角区域不被拉伸
            Vector4 border = new Vector4(radius, radius, radius, radius);
            return Sprite.Create(
                tex,
                new Rect(0, 0, s, s),
                new Vector2(0.5f, 0.5f),
                100f,
                0u,
                SpriteMeshType.FullRect,
                border
            );
        }
    }
}
