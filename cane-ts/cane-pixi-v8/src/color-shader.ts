import { generateTextureBatchBit, generateTextureBatchBitGl, type HighShaderBit } from "pixi.js";

export const FLAG_LINEAR_BLEND_V1 = 16;

/** Convert canonical texels before filtering; both batch families share this path. */
export function caneTextureBitV1(maxTextures: number, light: boolean, gpu: boolean): HighShaderBit {
  const base = gpu ? generateTextureBatchBit(maxTextures) : generateTextureBatchBitGl(maxTextures);
  const flags = light ? (gpu ? "vCaneFlags" : "floor(vCaneFlags + 0.5)")
    : (gpu ? "u32(round(vDarkAndFlags.a * 255.0))" : "floor(vDarkAndFlags.a * 255.0 + 0.5)");
  const main = Array.from({ length: maxTextures }, (_, i) => gpu
    ? `${i === 0 ? "if" : "else if"} (vTextureId == ${i}u) { outColor = caneSample(textureSource${i + 1}, vUV, ${flags}, vCaneSampling, caneFootprint); }`
    : `${i === 0 ? "if" : "else if"} (vTextureId < ${i}.5) { outColor = caneSample(uTextures[${i}], vUV, ${flags}, vCaneSampling, caneFootprint); }`).join("\n");
  return {
    name: `cane-textures-${light ? "light" : "two-color"}`,
    vertex: {
      ...base.vertex,
      header: `${base.vertex?.header ?? ""}\n${gpu
        ? "@in aCaneSampling: vec3<f32>; @out @interpolate(flat) vCaneSampling: vec3<f32>;"
        : "in vec3 aCaneSampling; out vec3 vCaneSampling;"}`,
      main: `${base.vertex?.main ?? ""}\nvCaneSampling = aCaneSampling;`,
    },
    fragment: {
      header: `${base.fragment?.header ?? ""}\n${gpu
        ? "@in @interpolate(flat) vCaneSampling: vec3<f32>;"
        : "in vec3 vCaneSampling;"}\n${gpu ? SAMPLE_WGSL : SAMPLE_GLSL}`,
      main: `${gpu
        ? "let caneFootprint = max(length(dpdx(vUV * vCaneSampling.xy)), length(dpdy(vUV * vCaneSampling.xy)));"
        : "float caneFootprint = max(length(dFdx(vUV * vCaneSampling.xy)), length(dFdy(vUV * vCaneSampling.xy)));"}\n${main}`,
    },
  };
}

const SAMPLE_GLSL = `
float caneDecodeChannel(float c) { return c <= .04045 ? c / 12.92 : pow((c + .055) / 1.055, 2.4); }
float caneEncodeChannel(float c) { return c <= .0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - .055; }
vec3 caneDecode(vec3 c) { return vec3(caneDecodeChannel(c.r), caneDecodeChannel(c.g), caneDecodeChannel(c.b)); }
vec3 caneEncode(vec3 c) { return vec3(caneEncodeChannel(c.r), caneEncodeChannel(c.g), caneEncodeChannel(c.b)); }
bool caneHas(float flags, float bit) { return mod(floor(flags / bit), 2.0) >= 1.0; }
float caneAddress(float p, float size, float mode) {
  if (mode < .5) return clamp(p, 0.0, size - 1.0);
  float period = mode < 1.5 ? size : size * 2.0;
  float wrapped = mod(p, period);
  return mode > 1.5 && wrapped >= size ? period - 1.0 - wrapped : wrapped;
}
vec4 caneTexel(sampler2D tex, vec2 p, vec3 info, float flags) {
  float settings = floor(info.z + .5);
  vec2 pixel = vec2(caneAddress(p.x, info.x, mod(floor(settings / 4.0), 4.0)),
                    caneAddress(p.y, info.y, floor(settings / 16.0)));
  vec4 c = texelFetch(tex, ivec2(pixel), 0);
  if (!caneHas(flags, 2.0)) c.rgb = caneDecode(c.rgb);
  if (!caneHas(flags, 16.0)) {
    if (caneHas(flags, 4.0)) c.rgb = c.a > .000001 ? caneEncode(c.rgb / c.a) * c.a : vec3(0.0);
    else c.rgb = caneEncode(c.rgb);
  }
  return c;
}
vec4 caneSample(sampler2D tex, vec2 uv, float flags, vec3 info, float footprint) {
  float settings = floor(info.z + .5);
  bool interpolate = caneHas(settings, footprint > 1.0 ? 1.0 : 2.0);
  if (!interpolate) return caneTexel(tex, floor(uv * info.xy), info, flags);
  vec2 point = uv * info.xy - .5, p = floor(point), f = fract(point);
  return mix(mix(caneTexel(tex, p, info, flags), caneTexel(tex, p + vec2(1,0), info, flags), f.x),
             mix(caneTexel(tex, p + vec2(0,1), info, flags), caneTexel(tex, p + vec2(1,1), info, flags), f.x), f.y);
}
`;

const SAMPLE_WGSL = `
fn caneDecodeChannel(c: f32) -> f32 { if (c <= .04045) { return c / 12.92; } return pow((c + .055) / 1.055, 2.4); }
fn caneEncodeChannel(c: f32) -> f32 { if (c <= .0031308) { return c * 12.92; } return 1.055 * pow(c, 1.0 / 2.4) - .055; }
fn caneDecode(c: vec3<f32>) -> vec3<f32> { return vec3<f32>(caneDecodeChannel(c.r), caneDecodeChannel(c.g), caneDecodeChannel(c.b)); }
fn caneEncode(c: vec3<f32>) -> vec3<f32> { return vec3<f32>(caneEncodeChannel(c.r), caneEncodeChannel(c.g), caneEncodeChannel(c.b)); }
fn caneAddress(p: i32, size: i32, mode: u32) -> i32 {
  if (mode == 0u) { return clamp(p, 0, size - 1); }
  let period = select(size, size * 2, mode == 2u);
  let wrapped = ((p % period) + period) % period;
  return select(wrapped, period - 1 - wrapped, mode == 2u && wrapped >= size);
}
fn caneTexel(tex: texture_2d<f32>, p: vec2<i32>, info: vec3<f32>, flags: u32) -> vec4<f32> {
  let settings = u32(round(info.z));
  let pixel = vec2<i32>(caneAddress(p.x, i32(info.x), (settings >> 2u) & 3u), caneAddress(p.y, i32(info.y), settings >> 4u));
  var c = textureLoad(tex, pixel, 0);
  if ((flags & 2u) == 0u) { c = vec4<f32>(caneDecode(c.rgb), c.a); }
  if ((flags & 16u) == 0u) {
    if ((flags & 4u) != 0u) {
      if (c.a > .000001) { c = vec4<f32>(caneEncode(c.rgb / c.a) * c.a, c.a); }
      else { c = vec4<f32>(0.0, 0.0, 0.0, c.a); }
    } else { c = vec4<f32>(caneEncode(c.rgb), c.a); }
  }
  return c;
}
fn caneSample(tex: texture_2d<f32>, uv: vec2<f32>, flags: u32, info: vec3<f32>, footprint: f32) -> vec4<f32> {
  let settings = u32(round(info.z));
  let interpolate = (settings & select(2u, 1u, footprint > 1.0)) != 0u;
  if (!interpolate) { return caneTexel(tex, vec2<i32>(floor(uv * info.xy)), info, flags); }
  let point = uv * info.xy - .5;
  let p = vec2<i32>(floor(point)); let f = fract(point);
  return mix(mix(caneTexel(tex, p, info, flags), caneTexel(tex, p + vec2<i32>(1,0), info, flags), f.x),
             mix(caneTexel(tex, p + vec2<i32>(0,1), info, flags), caneTexel(tex, p + vec2<i32>(1,1), info, flags), f.x), f.y);
}
`;
