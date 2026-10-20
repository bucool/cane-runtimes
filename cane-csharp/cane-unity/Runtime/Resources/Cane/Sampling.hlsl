#ifdef CANE_SAMPLE_CONTROL
float4 CaneLevel(float2 uv, float level, bool filtered) {
    if (!filtered) {
        float2 size = max(1, floor(_MainTex_TexelSize.zw * exp2(-level)));
        uv = (floor(uv * size) + .5) / size;
    }
    return tex2Dlod(_MainTex, float4(uv, 0, level));
}
float4 CaneSample(float2 uv) {
    float2 dx = ddx(uv) * _MainTex_TexelSize.zw, dy = ddy(uv) * _MainTex_TexelSize.zw;
    float lod = .5 * log2(max(1e-16, max(dot(dx, dx), dot(dy, dy))));
    bool filtered = (lod > 0 ? _Sampling.x : _Sampling.y) != 0;
    lod = _Sampling.z < 0 ? 0 : clamp(lod, 0, _Sampling.w);
    if (_Sampling.z <= 0) return CaneLevel(uv, floor(lod + .5), filtered);
    float level = floor(lod);
    return lerp(CaneLevel(uv, level, filtered), CaneLevel(uv, min(level + 1, _Sampling.w), filtered), frac(lod));
}
#else
float4 CaneSample(float2 uv) { return tex2D(_MainTex, uv); }
#endif
