using UnityEngine;

namespace Cane.Unity
{
    public static class SpriteAttachment
    {
        /// <summary>Retains Sprite pivots, tight geometry and packed atlas UVs. Scale is in Cane pixels per source pixel.</summary>
        public static MeshAttachment Create(Sprite sprite, int texture, double scale = 1)
        {
            var vertices = sprite.vertices; var uv = sprite.uv; var triangles = sprite.triangles;
            var positions = new double[vertices.Length * 2]; var uvs = new float[positions.Length]; var indices = new uint[triangles.Length];
            double pixels = sprite.pixelsPerUnit * scale;
            for (int i = 0; i < vertices.Length; i++)
            {
                positions[i * 2] = vertices[i].x * pixels; positions[i * 2 + 1] = vertices[i].y * pixels;
                uvs[i * 2] = uv[i].x; uvs[i * 2 + 1] = 1 - uv[i].y;
            }
            for (int i = 0; i < triangles.Length; i++) indices[i] = triangles[i];
            return new MeshAttachment(sprite.name, new Geometry(positions), texture, uvs, indices);
        }
    }
}
