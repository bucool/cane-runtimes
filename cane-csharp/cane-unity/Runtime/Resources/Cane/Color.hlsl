float3 CaneToLinear(float3 value) {
    return lerp(value / 12.92, pow(max((value + .055) / 1.055, 0), 2.4), step(.04045, value));
}
float3 CaneToSrgb(float3 value) {
    return lerp(value * 12.92, 1.055 * pow(max(value, 0), 1.0 / 2.4) - .055, step(.0031308, value));
}
