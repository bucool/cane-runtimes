using System;

namespace Cane
{
    /// <summary>An integer texel rectangle in Cane's top-left texture coordinates.</summary>
    public readonly struct TextureRegion
    {
        public readonly int X, Y, Width, Height;
        public TextureRegion(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        public TextureRegion Union(TextureRegion other)
        {
            int x = Math.Min(X, other.X), y = Math.Min(Y, other.Y);
            return new TextureRegion(x, y, Math.Max(X + Width, other.X + other.Width) - x, Math.Max(Y + Height, other.Y + other.Height) - y);
        }
        /// <summary>Expands a crop to a shared integer texel grid without moving its contents.</summary>
        public TextureRegion Align(int stepX, int stepY)
        {
            int x = (int)Math.Floor((double)X / stepX) * stepX, y = (int)Math.Floor((double)Y / stepY) * stepY;
            return new TextureRegion(x, y, (int)Math.Ceiling((double)(X + Width) / stepX) * stepX - x,
                (int)Math.Ceiling((double)(Y + Height) / stepY) * stepY - y);
        }
        public TextureRegion Transform(double scaleX, double scaleY, double offsetX, double offsetY)
        {
            double ax = X * scaleX + offsetX, bx = (X + Width) * scaleX + offsetX;
            double ay = Y * scaleY + offsetY, by = (Y + Height) * scaleY + offsetY;
            int x = (int)Math.Floor(Math.Min(ax, bx)), y = (int)Math.Floor(Math.Min(ay, by));
            return new TextureRegion(x, y, (int)Math.Ceiling(Math.Max(ax, bx)) - x, (int)Math.Ceiling(Math.Max(ay, by)) - y);
        }
        public static TextureRegion Bounds(ReadOnlySpan<float> uvs, int width, int height)
        {
            if (uvs.IsEmpty) return default;
            double minX = uvs[0], minY = uvs[1], maxX = minX, maxY = minY;
            for (int i = 2; i < uvs.Length; i += 2)
            {
                minX = Math.Min(minX, uvs[i]); maxX = Math.Max(maxX, uvs[i]);
                minY = Math.Min(minY, uvs[i + 1]); maxY = Math.Max(maxY, uvs[i + 1]);
            }
            int x = (int)Math.Floor(minX * width), y = (int)Math.Floor(minY * height);
            return new TextureRegion(x, y, Math.Max(1, (int)Math.Ceiling(maxX * width) - x), Math.Max(1, (int)Math.Ceiling(maxY * height) - y));
        }
        /// <summary>Moves texels to another atlas without scaling the image or changing geometry.</summary>
        public float[] RemapUvs(ReadOnlySpan<float> uvs, int sourceWidth, int sourceHeight, int targetX, int targetY, int targetWidth, int targetHeight)
        {
            var result = new float[uvs.Length];
            for (int i = 0; i < result.Length; i += 2)
            {
                result[i] = (float)(((double)uvs[i] * sourceWidth - X + targetX) / targetWidth);
                result[i + 1] = (float)(((double)uvs[i + 1] * sourceHeight - Y + targetY) / targetHeight);
            }
            return result;
        }
    }
}
