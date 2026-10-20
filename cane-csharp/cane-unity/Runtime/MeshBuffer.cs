using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Cane.Unity
{
    /// <summary>Uploads Core's final geometry. Adjacent compatible draws share a submesh.</summary>
    public sealed class MeshBuffer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex { public Vector3 Position; public Color Light; public Vector2 Uv; public Vector3 Dark; }
        private static readonly VertexAttributeDescriptor[] Layout = {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 3)
        };
        private static readonly VertexAttributeDescriptor[] CanvasLayout = {
            Layout[0], Layout[1], Layout[2], Layout[3],
            new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.Float32, 4, stream: 1)
        };
        private const MeshUpdateFlags Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
        public readonly Mesh Mesh = new Mesh { name = "Cane geometry", hideFlags = HideFlags.DontSave };
        public readonly List<Material> Materials = new List<Material>();
        public int VertexCount { get; private set; }
        public int IndexCount { get; private set; }
        private Vertex[] vertices = Array.Empty<Vertex>();
        private Color[] canvasLight = Array.Empty<Color>();
        private Vector3[] normals = Array.Empty<Vector3>();
        private Vector4[] tangents = Array.Empty<Vector4>(), tangentBasis = Array.Empty<Vector4>();
        private bool hasNormals, hasTangents;
        private readonly bool canvas;
        private uint[] indices = Array.Empty<uint>();
        private ushort[] smallIndices = Array.Empty<ushort>();
        private bool wideIndices;
        private (uint[] Indices, int Vertices, int Count, Material Material)[] plan = Array.Empty<(uint[], int, int, Material)>();
        private readonly List<SubMeshDescriptor> submeshes = new List<SubMeshDescriptor>();
        private int previousDrawCount = -1;
        public MeshBuffer(bool canvas = false) { this.canvas = canvas; Mesh.MarkDynamic(); }

        public bool Update(IReadOnlyList<Draw> draws, CaneResources resources, float scale, float zSpacing = 0, Material[] slotMaterials = null, int start = 0, int end = int.MaxValue, SpriteMaskInteraction mask = SpriteMaskInteraction.None, bool addNormals = false, bool solveTangents = false)
        {
            end = Math.Min(end, draws.Count); int count = end - start, vc = 0, ic = 0;
            for (int i = start; i < end; i++) { vc += draws[i].VertexCount; ic += draws[i].IndexCount; }
            bool topology = previousDrawCount != count;
            bool wide = vc > 65535;
            addNormals |= solveTangents;
            bool layoutChanged = hasNormals != addNormals || hasTangents != solveTangents;
            if (vertices.Length < vc)
            {
                Array.Resize(ref vertices, wide ? Capacity(vc) : Math.Min(65535, Capacity(vc)));
                if (canvas) Array.Resize(ref canvasLight, vertices.Length);
                layoutChanged = true;
            }
            if (layoutChanged)
            {
                hasNormals = addNormals; hasTangents = solveTangents; topology = true;
                var layout = canvas ? CanvasLayout : Layout; int stream = canvas ? 2 : 1;
                if (hasNormals)
                {
                    int at = layout.Length; Array.Resize(ref layout, at + (hasTangents ? 2 : 1));
                    layout[at] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, stream);
                    if (hasTangents) layout[at + 1] = new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4, stream + 1);
                    if (normals.Length < vertices.Length) { Array.Resize(ref normals, vertices.Length); Array.Fill(normals, Vector3.back); }
                    if (hasTangents && tangents.Length < vertices.Length) { Array.Resize(ref tangents, vertices.Length); Array.Resize(ref tangentBasis, vertices.Length); }
                }
                Mesh.SetVertexBufferParams(vertices.Length, layout);
                if (hasNormals && vertices.Length > 0) Mesh.SetVertexBufferData(normals, 0, 0, vertices.Length, stream, Flags);
            }
            if (wide != wideIndices || (wide ? indices.Length : smallIndices.Length) < ic)
            {
                wideIndices = wide;
                if (wide) { if (indices.Length < ic) Array.Resize(ref indices, Capacity(ic)); Mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32); }
                else { if (smallIndices.Length < ic) Array.Resize(ref smallIndices, Capacity(ic)); Mesh.SetIndexBufferParams(smallIndices.Length, IndexFormat.UInt16); }
                topology = true;
            }
            if (plan.Length < count) Array.Resize(ref plan, Capacity(count));
            for (int i = start; i < end; i++)
            {
                var draw = draws[i]; var material = slotMaterials != null && draw.Slot < slotMaterials.Length ? slotMaterials[draw.Slot] : null;
                if (material == null) material = resources.Material(draw.Texture, draw.Blend, canvas, mask: mask);
                ref var old = ref plan[i - start];
                topology |= draw.Clipped || old.Indices != draw.Indices || old.Vertices != draw.VertexCount || old.Count != draw.IndexCount || !ReferenceEquals(old.Material, material);
                old = (draw.Indices, draw.VertexCount, draw.IndexCount, material);
            }
            if (topology) { Materials.Clear(); submeshes.Clear(); }
            int vertex = 0, index = 0;
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            for (int i = start; i < end; i++)
            {
                var draw = draws[i]; var material = plan[i - start].Material;
                if (topology)
                {
                    if (Materials.Count == 0 || !ReferenceEquals(Materials[Materials.Count - 1], material))
                    { Materials.Add(material); submeshes.Add(new SubMeshDescriptor(index, 0) { firstVertex = vertex }); }
                    int last = submeshes.Count - 1; var sub = submeshes[last]; sub.indexCount += draw.IndexCount; sub.vertexCount += draw.VertexCount; submeshes[last] = sub;
                    if (wide) for (int j = 0; j < draw.IndexCount; j++) indices[index + j] = (uint)vertex + draw.Indices[j];
                    else for (int j = 0; j < draw.IndexCount; j++) smallIndices[index + j] = (ushort)(vertex + draw.Indices[j]);
                }
                var light = new Color(draw.Light[0], draw.Light[1], draw.Light[2], draw.Light[3]);
                // Canvas multiplies COLOR by Graphic tint and CanvasGroup alpha. Keep Core tint separate.
                if (canvas) { Array.Fill(canvasLight, light, vertex, draw.VertexCount); light = Color.white; }
                var dark = new Vector3(draw.Dark[0], draw.Dark[1], draw.Dark[2]) * draw.Dark[3];
                var positions = draw.Vertices; var uvs = draw.Uvs; float z = -i * zSpacing;
                for (int j = 0, p = 0; j < draw.VertexCount; j++, p += 2)
                {
                    float x = positions[p] * scale, y = positions[p + 1] * scale;
                    if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
                    ref var target = ref vertices[vertex + j]; target.Position.x = x; target.Position.y = y; target.Position.z = z;
                    target.Uv.x = uvs[p]; target.Uv.y = 1 - uvs[p + 1]; target.Light = light; target.Dark = dark;
                }
                vertex += draw.VertexCount; index += draw.IndexCount;
            }
            float zFirst = -start * zSpacing, zLast = -(end - 1) * zSpacing;
            var bounds = vc == 0 ? new UnityEngine.Bounds() : new UnityEngine.Bounds(new Vector3((minX + maxX) * .5f, (minY + maxY) * .5f, (zFirst + zLast) * .5f), new Vector3(maxX - minX, maxY - minY, Math.Abs(zLast - zFirst)));
            if (vc > 0) Mesh.SetVertexBufferData(vertices, 0, 0, vc, 0, Flags);
            if (canvas && vc > 0) Mesh.SetVertexBufferData(canvasLight, 0, 0, vc, 1, Flags);
            if (hasTangents && vc > 0) UpdateTangents(vc, ic);
            if (topology)
            {
                if (ic > 0) { if (wide) Mesh.SetIndexBufferData(indices, 0, 0, ic, Flags); else Mesh.SetIndexBufferData(smallIndices, 0, 0, ic, Flags); }
                for (int i = 0; i < submeshes.Count; i++) { var sub = submeshes[i]; sub.bounds = bounds; submeshes[i] = sub; }
                // The subMeshCount setter can shrink the index buffer when draws disappear.
                Mesh.SetSubMeshes(submeshes, Flags);
            }
            Mesh.bounds = bounds; VertexCount = vc; IndexCount = ic; previousDrawCount = count; return topology;
        }
        private void UpdateTangents(int vertexCount, int indexCount)
        {
            Array.Clear(tangentBasis, 0, vertexCount);
            for (int i = 0; i < indexCount; i += 3)
            {
                int a = wideIndices ? (int)indices[i] : smallIndices[i], b = wideIndices ? (int)indices[i + 1] : smallIndices[i + 1], c = wideIndices ? (int)indices[i + 2] : smallIndices[i + 2];
                ref var va = ref vertices[a]; ref var vb = ref vertices[b]; ref var vc = ref vertices[c];
                float abx = vb.Position.x - va.Position.x, aby = vb.Position.y - va.Position.y;
                float acx = vc.Position.x - va.Position.x, acy = vc.Position.y - va.Position.y;
                float ub = vb.Uv.x - va.Uv.x, vbY = vb.Uv.y - va.Uv.y;
                float uc = vc.Uv.x - va.Uv.x, vcY = vc.Uv.y - va.Uv.y;
                float determinant = ub * vcY - vbY * uc;
                if (determinant == 0) continue;
                float tx = (abx * vcY - acx * vbY) / determinant, ty = (aby * vcY - acy * vbY) / determinant;
                float bx = (acx * ub - abx * uc) / determinant, by = (acy * ub - aby * uc) / determinant;
                ref var ta = ref tangentBasis[a]; ta.x += tx; ta.y += ty; ta.z += bx; ta.w += by;
                ref var tb = ref tangentBasis[b]; tb.x += tx; tb.y += ty; tb.z += bx; tb.w += by;
                ref var tc = ref tangentBasis[c]; tc.x += tx; tc.y += ty; tc.z += bx; tc.w += by;
            }
            for (int i = 0; i < vertexCount; i++)
            {
                var basis = tangentBasis[i]; float length = Mathf.Sqrt(basis.x * basis.x + basis.y * basis.y);
                tangents[i] = length == 0 ? new Vector4(1, 0, 0, -1) : new Vector4(basis.x / length, basis.y / length, 0, basis.y * basis.z - basis.x * basis.w < 0 ? -1 : 1);
            }
            Mesh.SetVertexBufferData(tangents, 0, 0, vertexCount, canvas ? 3 : 2, Flags);
        }
        private static int Capacity(int count) { int capacity = 16; while (capacity < count) capacity *= 2; return capacity; }
        public void Dispose() => CaneResources.Destroy(Mesh);
    }
}
