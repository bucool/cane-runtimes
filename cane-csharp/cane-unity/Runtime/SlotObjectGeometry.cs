using System;
using UnityEngine;

namespace Cane.Unity
{
    // Shared projection for scene Renderers and Canvas Graphics. Core owns the
    // clipping partition and hit test; Unity owns its GPU buffer and transforms.
    internal sealed class SlotObjectGeometry : IDisposable
    {
        internal static readonly int AlphaId = Shader.PropertyToID("_CaneSlotAlpha");
        private static readonly int Count = Shader.PropertyToID("_CaneClipCount"), Inverse = Shader.PropertyToID("_CaneClipInverse"),
            Matrix = Shader.PropertyToID("_CaneWorldToSlot"), Vertices = Shader.PropertyToID("_CaneClipVertices");
        private readonly ClippingBoundary boundary = new ClippingBoundary();
        private GraphicsBuffer buffer;
        private Vector2[] vertices = Array.Empty<Vector2>();
        private int count = -1;
        private float alpha;
        private Matrix4x4 worldToSlot;

        internal void Update(CanePlayer player, int slot, bool before, float opacity = 1)
        {
            bool clipped = player.Skeleton.Frame.Clipping(slot, before, boundary);
            var indices = clipped ? boundary.Triangles : default;
            int length = clipped ? indices.Length : -1; bool changed = count != length;
            if (buffer == null || length > vertices.Length)
            {
                vertices = new Vector2[Mathf.NextPowerOfTwo(Math.Max(4, length))]; buffer?.Dispose();
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, vertices.Length, 8); changed = true;
            }
            for (int i = 0; i < length; i++)
            {
                int at = indices[i] * 2; var value = new Vector2(boundary.Vertices[at], boundary.Vertices[at + 1]);
                changed |= vertices[i].x != value.x || vertices[i].y != value.y; vertices[i] = value;
            }
            if (changed && length > 0) buffer.SetData(vertices, 0, 0, length);
            count = length;
            float units = player.UnitsPerPixel == 0 ? 0 : 1 / player.UnitsPerPixel;
            worldToSlot = Matrix4x4.Scale(new Vector3(units, units, 1)) * player.transform.worldToLocalMatrix;
            alpha = Mathf.Clamp01((float)player.Skeleton.SlotOpacity(slot)) * opacity;
        }
        internal void Apply(MaterialPropertyBlock block)
        {
            block.SetFloat(AlphaId, alpha); block.SetInteger(Count, count); block.SetFloat(Inverse, boundary.Inverse ? 1 : 0);
            block.SetMatrix(Matrix, worldToSlot); block.SetBuffer(Vertices, buffer);
        }
        internal void Apply(Material material, Transform canvasSpace = null)
        {
            material.SetFloat(AlphaId, alpha); material.SetInteger(Count, count); material.SetFloat(Inverse, boundary.Inverse ? 1 : 0);
            // Overlay UI shaders receive positions relative to their root Canvas.
            material.SetMatrix(Matrix, canvasSpace == null ? worldToSlot : worldToSlot * canvasSpace.localToWorldMatrix); material.SetBuffer(Vertices, buffer);
        }
        internal bool ContainsPoint(Vector3 world)
        {
            var point = worldToSlot.MultiplyPoint3x4(world);
            return count < 0 || boundary.ContainsPoint(point.x, point.y);
        }
        internal static void Follow(Transform left, Transform right, double[] matrix, int at, float units)
        {
            double a = matrix[at], b = matrix[at + 1], c = matrix[at + 2], d = matrix[at + 3];
            // Two ordinary Transforms represent the full 2D affine, including shear.
            double turn = .5 * Math.Atan2(2 * (a * c + b * d), a * a + b * b - c * c - d * d);
            double cosine = Math.Cos(turn), sine = Math.Sin(turn), x = a * cosine + c * sine, y = b * cosine + d * sine;
            double scale = Math.Sqrt(x * x + y * y);
            left.localPosition = new Vector3((float)matrix[at + 4] * units, (float)matrix[at + 5] * units, 0);
            left.localRotation = Quaternion.Euler(0, 0, (float)(Math.Atan2(y, x) * 180 / Math.PI));
            left.localScale = new Vector3((float)scale, scale == 0 ? 0 : (float)((a * d - b * c) / scale), 1);
            right.localRotation = Quaternion.Euler(0, 0, (float)(-turn * 180 / Math.PI));
        }
        public void Dispose() { buffer?.Dispose(); buffer = null; vertices = Array.Empty<Vector2>(); count = -1; }
    }
}
