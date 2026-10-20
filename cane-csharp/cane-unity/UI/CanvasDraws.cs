using System;
using System.Collections.Generic;

namespace Cane.Unity
{
    // CanvasRenderer accepts only 16-bit indices. Split oversized draws without discarding triangles.
    internal sealed class CanvasDraws
    {
        internal const int Limit = 60000;
        internal readonly List<Draw> Draws = new List<Draw>();
        private readonly List<Draw> chunks = new List<Draw>();
        internal void Update(IReadOnlyList<Draw> input)
        {
            Draws.Clear(); int chunk = 0;
            for (int draw = 0; draw < input.Count; draw++)
            {
                var source = input[draw];
                if (source.VertexCount <= Limit) { Draws.Add(source); continue; }
                for (int start = 0; start < source.IndexCount; start += Limit)
                {
                    if (chunk == chunks.Count) chunks.Add(new Draw());
                    var target = chunks[chunk++]; int count = Math.Min(Limit, source.IndexCount - start);
                    if (target.Indices.Length < count)
                    {
                        target.Vertices = new float[count * 2]; target.Uvs = new float[count * 2]; target.Indices = new uint[count];
                        for (uint i = 0; i < count; i++) target.Indices[i] = i;
                    }
                    target.VertexCount = target.IndexCount = count; target.Slot = source.Slot; target.Attachment = source.Attachment;
                    target.Texture = source.Texture; target.Blend = source.Blend;
                    source.Light.CopyTo(target.Light, 0); source.Dark.CopyTo(target.Dark, 0);
                    for (int i = 0; i < count; i++)
                    {
                        int from = (int)source.Indices[start + i] * 2, to = i * 2;
                        target.Vertices[to] = source.Vertices[from]; target.Vertices[to + 1] = source.Vertices[from + 1];
                        target.Uvs[to] = source.Uvs[from]; target.Uvs[to + 1] = source.Uvs[from + 1];
                    }
                    Draws.Add(target);
                }
            }
        }
    }
}
