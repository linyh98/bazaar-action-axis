using System;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    // Only the center symbol is ours; the surrounding skin comes from BPP.
    internal static class DockButtonArtwork
    {
        public static Texture2D CreateIcon()
        {
            const int size = 128;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    Color sum = new Color(0, 0, 0, 0);
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            sum += Sample((x + (sx + 0.5f) * 0.5f) / size * 2f - 1f,
                                (y + (sy + 0.5f) * 0.5f) / size * 2f - 1f);
                    pixels[y * size + x] = sum * 0.25f;
                }
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "ActionAxis_DockTimelineIcon";
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        internal static Color Sample(float x, float y)
        {
            if (!Contains(x, y)) return new Color(0, 0, 0, 0);
            // The warm ivory matches the weight and color of BPP's book icon.
            float light = (y + 1f) * 0.5f;
            return new Color(0.93f + light * 0.07f, 0.79f + light * 0.16f, 0.48f + light * 0.25f, 1f);
        }

        private static bool Contains(float x, float y)
        {
            float dy = y - 0.20f;
            float radius = (float)Math.Sqrt(x * x + dy * dy);
            if (Math.Abs(radius - 0.55f) <= 0.10f) return true;
            if (Distance(x, y, 0, 0.20f, 0, 0.55f) <= 0.075f ||
                Distance(x, y, 0, 0.20f, 0.27f, 0.04f) <= 0.075f) return true;
            if (Math.Abs(y + 0.66f) <= 0.065f && Math.Abs(x) <= 0.77f) return true;
            return y >= -0.87f && y <= -0.48f &&
                (Math.Abs(x) <= 0.06f || Math.Abs(x - 0.51f) <= 0.06f || Math.Abs(x + 0.51f) <= 0.06f);
        }

        private static float Distance(float x, float y, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float t = Math.Max(0, Math.Min(1, ((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy)));
            dx = x - ax - t * dx; dy = y - ay - t * dy;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
