#ifndef CANE_SLOT_OBJECT_INCLUDED
#define CANE_SLOT_OBJECT_INCLUDED
#ifndef CANE_SLOT_MATERIAL_PROPERTIES
float _CaneSlotAlpha;
int _CaneClipCount;
#endif
float _CaneClipInverse;
float4x4 _CaneWorldToSlot;
// Surface Shader analysis needs the same input dependencies but cannot parse buffers.
#ifdef SHADER_TARGET_SURFACE_ANALYSIS
float2 _CaneClipVertices[3];
#else
StructuredBuffer<float2> _CaneClipVertices;
#endif
float CaneClipCross(float2 a, float2 b, float2 p) { return (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x); }

float4 CaneApplySlot(float3 world, float4 color) {
    if (_CaneClipCount >= 0) {
        float2 samplePosition = mul(_CaneWorldToSlot, float4(world, 1)).xy;
        bool inside = false;
        for (int i = 0; i < _CaneClipCount; i += 3) {
            float2 a = _CaneClipVertices[i], b = _CaneClipVertices[i + 1], c = _CaneClipVertices[i + 2];
            float ab = CaneClipCross(a, b, samplePosition), bc = CaneClipCross(b, c, samplePosition), ca = CaneClipCross(c, a, samplePosition);
            if (CaneClipCross(a, b, c) != 0 && ((ab >= 0 && bc >= 0 && ca >= 0) || (ab <= 0 && bc <= 0 && ca <= 0))) { inside = true; break; }
        }
        clip(inside != (_CaneClipInverse != 0) ? 1 : -1);
    }
    return color * _CaneSlotAlpha;
}
#endif
