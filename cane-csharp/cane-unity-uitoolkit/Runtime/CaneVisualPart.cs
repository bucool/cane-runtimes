using System;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cane.Unity
{
    internal sealed class CaneVisualPart : VisualElement
    {
        private static readonly Dictionary<Material, (Material Material, int Users)> sharedMaterials = new Dictionary<Material, (Material, int)>();
        private List<Draw> draws;
        private int first, end;
        private Material source, material;
        private bool shared;
        private Vector2 origin;
        private float scale;
        internal CaneVisualPart()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = style.top = style.right = style.bottom = 0;
            generateVisualContent += DrawMesh;
        }
        internal static bool Compatible(Draw a, Draw b)
        {
            if (a.Texture != b.Texture || a.Blend != b.Blend) return false;
            for (int i = 0; i < 4; i++) if (a.Light[i] != b.Light[i] || a.Dark[i] != b.Dark[i]) return false;
            return true;
        }
        internal void Update(List<Draw> values, int start, int stop, CaneResources resources, Vector2 offset, float size)
        {
            draws = values; first = start; end = stop; origin = offset; scale = size;
            var draw = draws[first];
            var next = resources.Material(draw.Texture, draw.Blend);
            bool white = draw.Light[0] == 1 && draw.Light[1] == 1 && draw.Light[2] == 1 && draw.Light[3] == 1 &&
                (draw.Dark[3] == 0 || draw.Dark[0] == 0 && draw.Dark[1] == 0 && draw.Dark[2] == 0);
            if (source != next || shared && !white)
            {
                ReleaseMaterial(); source = next; shared = white;
                if (white && sharedMaterials.TryGetValue(source, out var entry))
                {
                    material = entry.Material; entry.Users++; sharedMaterials[source] = entry;
                }
                else
                {
                    material = new Material(Resources.Load<Shader>("Cane/UIToolkit")) { hideFlags = HideFlags.DontSave };
                    material.CopyPropertiesFromMaterial(source);
                    if (white)
                    {
                        material.SetVector("_Light", Vector4.one); material.SetVector("_Dark", Vector4.zero);
                        sharedMaterials.Add(source, (material, 1));
                    }
                }
                style.unityMaterial = new StyleMaterialDefinition(material);
            }
            // Once tinted, keep the private material so color animation does not allocate on each transition.
            if (!shared)
            {
                material.SetVector("_Light", new Vector4(draw.Light[0], draw.Light[1], draw.Light[2], draw.Light[3]));
                material.SetVector("_Dark", new Vector4(draw.Dark[0], draw.Dark[1], draw.Dark[2], 0) * draw.Dark[3]);
            }
            MarkDirtyRepaint();
        }
        private unsafe void DrawMesh(MeshGenerationContext context)
        {
            if (draws == null) return;
            for (int start = first; start < end;)
            {
                var draw = draws[start];
                bool expand = draw.VertexCount > 65535;
                int stop = start + 1, vertexCount = draw.VertexCount, indexCount = draw.IndexCount;
                // Adjacent compatible draws share one UI mesh, within its 16-bit vertex limit.
                if (!expand) while (stop < end && vertexCount + draws[stop].VertexCount <= 65535)
                {
                    vertexCount += draws[stop].VertexCount; indexCount += draws[stop++].IndexCount;
                }
                // A single oversized draw is expanded and split at triangle boundaries.
                int batch = expand ? 60000 : indexCount;
                for (int offset = 0; offset < indexCount; offset += batch)
                {
                    int count = Mathf.Min(batch, indexCount - offset), vertexAt = 0, indexAt = 0;
                    context.AllocateTempMesh(expand ? count : vertexCount, count, out var vertexMemory, out var indexMemory);
                    // These contiguous views live only inside this mesh-generation callback.
                    var vertices = new Span<Vertex>(NativeSliceUnsafeUtility.GetUnsafePtr(vertexMemory), vertexMemory.Length);
                    var indices = new Span<ushort>(NativeSliceUnsafeUtility.GetUnsafePtr(indexMemory), indexMemory.Length);
                    for (int part = start; part < stop; part++)
                    {
                        draw = draws[part];
                        int vertexLength = expand ? count : draw.VertexCount, indexLength = expand ? count : draw.IndexCount;
                        for (int i = 0; i < vertexLength; i++)
                        {
                            int p = (expand ? (int)draw.Indices[offset + i] : i) * 2;
                            vertices[vertexAt + i] = new Vertex {
                                position = new Vector3(origin.x + draw.Vertices[p] * scale, origin.y - draw.Vertices[p + 1] * scale, Vertex.nearZ),
                                uv = new Vector2(draw.Uvs[p], 1 - draw.Uvs[p + 1]), tint = new Color32(255, 255, 255, 255)
                            };
                        }
                        for (int i = 0; i < indexLength; i += 3)
                        {
                            ushort a = (ushort)(vertexAt + (expand ? i : (int)draw.Indices[i]));
                            ushort b = (ushort)(vertexAt + (expand ? i + 1 : (int)draw.Indices[i + 1]));
                            ushort c = (ushort)(vertexAt + (expand ? i + 2 : (int)draw.Indices[i + 2]));
                            var ab = vertices[b].position - vertices[a].position; var ac = vertices[c].position - vertices[a].position;
                            // Final geometry may be reflected; UI Toolkit expects clockwise screen winding.
                            if (ab.x * ac.y - ab.y * ac.x < 0) { var swap = b; b = c; c = swap; }
                            indices[indexAt + i] = a; indices[indexAt + i + 1] = b; indices[indexAt + i + 2] = c;
                        }
                        vertexAt += vertexLength; indexAt += indexLength;
                    }
                    context.DrawMesh(vertexMemory, indexMemory, material.mainTexture, TextureOptions.SkipDynamicAtlas);
                }
                start = stop;
            }
        }
        private void ReleaseMaterial()
        {
            if (shared)
            {
                var entry = sharedMaterials[source];
                if (--entry.Users == 0) { sharedMaterials.Remove(source); CaneVisualElement.Destroy(material); }
                else sharedMaterials[source] = entry;
            }
            else CaneVisualElement.Destroy(material);
            material = source = null; shared = false;
        }
        internal void Dispose() { ReleaseMaterial(); draws = null; }
    }
}
