#ifndef CANE_MATERIAL_EFFECTS
#define CANE_MATERIAL_EFFECTS
#ifndef CANE_EFFECTS_MATERIAL_PROPERTIES
float4 _FillColor, _OutlineColor, _OutlineUvRect;
float _Grayscale, _OutlineWidth, _OutlineScreenSpace, _OutlineOnly;
float4 _SecondaryColor, _RimColor;
float _Effects, _Hue, _Saturation, _Brightness, _RampStrength, _RimPower;
#endif

sampler2D _SecondaryTex, _LightRamp;
float3 CaneColorAdjust(float3 rgb) {
    #ifndef UNITY_COLORSPACE_GAMMA
    rgb = CaneToSrgb(rgb);
    #endif
    float value = max(rgb.r, max(rgb.g, rgb.b)), low = min(rgb.r, min(rgb.g, rgb.b));
    float chroma = value - low, hue = 0;
    if (chroma > 0) hue = rgb.r == value ? (rgb.g - rgb.b) / chroma :
        rgb.g == value ? 2 + (rgb.b - rgb.r) / chroma : 4 + (rgb.r - rgb.g) / chroma;
    hue = frac((hue + _Hue / 60) / 6) * 6;
    float saturation = value > 0 ? saturate(chroma / value * _Saturation) : 0;
    float3 wheel = saturate(abs(fmod(hue + float3(0, 4, 2), 6) - 3) - 1);
    rgb = lerp(1, wheel, saturation) * value * _Brightness;
    #ifndef UNITY_COLORSPACE_GAMMA
    rgb = CaneToLinear(rgb);
    #endif
    return rgb;
}
float3 CaneLightRamp(float cosine) { return tex2D(_LightRamp, float2(cosine, .5)).rgb; }
float3 CaneRim(float3 normal, float3 view) {
    return _RimColor.rgb * _RimColor.a * pow(1 - saturate(dot(normal, view)), _RimPower);
}
float CaneOutlineAlpha(float2 uv) {
    float2 inside = step(_OutlineUvRect.xy, uv) * step(uv, _OutlineUvRect.zw);
    float2 inset = _MainTex_TexelSize.xy * .5;
    return CaneSample(clamp(uv, _OutlineUvRect.xy + inset, _OutlineUvRect.zw - inset)).a * inside.x * inside.y;
}
// Straight color and texture coverage enter here; attachment opacity is applied once afterward.
float4 CaneEffects(float4 color, float2 uv, float2 secondaryUv) {
    if (_SecondaryColor.a > 0) {
        float4 layer = tex2D(_SecondaryTex, secondaryUv);
        color.rgb = lerp(color.rgb, layer.rgb * _SecondaryColor.rgb, saturate(layer.a * _SecondaryColor.a));
    }
    if (_Hue != 0 || _Saturation != 1 || _Brightness != 1) color.rgb = CaneColorAdjust(color.rgb);
    if (_Grayscale > 0) {
        #ifdef UNITY_COLORSPACE_GAMMA
        float gray = dot(CaneToLinear(color.rgb), float3(.2126, .7152, .0722));
        float3 neutral = CaneToSrgb(gray.xxx);
        #else
        float gray = dot(color.rgb, float3(.2126, .7152, .0722));
        float3 neutral = gray.xxx;
        #endif
        color.rgb = lerp(color.rgb, neutral, saturate(_Grayscale));
    }
    color.rgb = lerp(color.rgb, _FillColor.rgb, saturate(_FillColor.a));
    float border = 0;
    if (_OutlineWidth > 0) {
        float2 dx = _OutlineScreenSpace != 0 ? ddx(uv) : float2(_MainTex_TexelSize.x, 0);
        float2 dy = _OutlineScreenSpace != 0 ? ddy(uv) : float2(0, _MainTex_TexelSize.y);
        dx *= _OutlineWidth; dy *= _OutlineWidth;
        float2 diagonal = (dx + dy) * .70710678, other = (dx - dy) * .70710678;
        float coverage = max(max(CaneOutlineAlpha(uv + dx), CaneOutlineAlpha(uv - dx)),
            max(CaneOutlineAlpha(uv + dy), CaneOutlineAlpha(uv - dy)));
        coverage = max(coverage, max(max(CaneOutlineAlpha(uv + diagonal), CaneOutlineAlpha(uv - diagonal)),
            max(CaneOutlineAlpha(uv + other), CaneOutlineAlpha(uv - other))));
        border = saturate(coverage - color.a) * saturate(_OutlineColor.a);
    }
    float original = _OutlineOnly != 0 ? 0 : color.a;
    float alpha = original + border;
    color = float4(alpha > 0 ? (color.rgb * original + _OutlineColor.rgb * border) / alpha : 0, alpha);
    return color;
}
#endif
