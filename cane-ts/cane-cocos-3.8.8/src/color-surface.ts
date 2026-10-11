import { Texture2D, director, gfx } from "cc";
import type { RuntimeBlendModeV1, RuntimeRenderPacketV1, RuntimeTextureV1 } from "@cane-runtime/core";
import { CaneCocosBlendColorSpaceV1 } from "./blend-color-space.js";
import { CANE_COCOS_FRAGMENT_V1 } from "./color-effect.js";
import { caneCocosColorFlagsV1 } from "./color-pipeline.js";
import { CaneCocosErrorV1 } from "./errors.js";
import { CaneCocosGeometryAssemblerV1, type CaneCocosColorModulationV1 } from "./geometry.js";
import { blendAlphaFactorsV1, blendFactorsV1 } from "./materials.js";
import type { CocosTextureStore } from "./texture-store.js";

/** A GFX-owned float texture presented through the ordinary UI texture contract. */
class ColorTextureV1 extends Texture2D {
  readonly framebuffer: gfx.Framebuffer;
  readonly #texture: gfx.Texture;
  readonly #size: readonly [number, number];
  readonly #sampler: gfx.Sampler;
  #released = false;

  constructor(device: gfx.Device, pass: gfx.RenderPass, width: number, height: number, sampler: gfx.Sampler, format: gfx.Format) {
    super();
    this.#size = [width, height];
    this.#sampler = sampler;
    this.#texture = device.createTexture(new gfx.TextureInfo(
      gfx.TextureType.TEX2D,
      gfx.TextureUsageBit.COLOR_ATTACHMENT | gfx.TextureUsageBit.SAMPLED | gfx.TextureUsageBit.TRANSFER_SRC,
      format, width, height,
    ));
    this.framebuffer = device.createFramebuffer(new gfx.FramebufferInfo(pass, [this.#texture]));
  }

  override get width(): number { return this.#size[0]; }
  override get height(): number { return this.#size[1]; }
  override getGFXTexture(): gfx.Texture { return this.#texture; }
  override getGFXSampler(): gfx.Sampler { return this.#sampler; }
  override destroy(): boolean {
    if (!this.#released) {
      this.framebuffer.destroy();
      this.#texture.destroy();
      this.#released = true;
    }
    return super.destroy();
  }
}

interface PreparedTextureV1 {
  readonly source: Texture2D;
  readonly gfxSource: gfx.Texture;
  readonly key: string;
  readonly texture: ColorTextureV1;
  readonly descriptor: gfx.DescriptorSet;
}

export interface CaneCocosSurfaceFrameV1 {
  readonly texture: Texture2D;
  readonly minX: number;
  readonly minY: number;
  readonly maxX: number;
  readonly maxY: number;
  /** V at the bottom of the host-space quad. */
  readonly bottomV: number;
}

/**
 * Adapter-local GPU color projection. It never samples animation or mutates a
 * Core packet. Canonical texels are converted before filtering; isolated packet
 * blending occurs in a real float target in the selected RGB domain.
 */
export class CaneCocosColorSurfaceV1 {
  readonly #device: gfx.Device;
  readonly #format: gfx.Format;
  readonly #pass: gfx.RenderPass;
  readonly #layout: gfx.DescriptorSetLayout;
  readonly #pipelineLayout: gfx.PipelineLayout;
  readonly #shader: gfx.Shader;
  readonly #normalizeShader: gfx.Shader;
  readonly #states = new Map<string, gfx.PipelineState>();
  readonly #command: gfx.CommandBuffer;
  readonly #vertices: gfx.Buffer;
  readonly #indices: gfx.Buffer;
  readonly #ia: gfx.InputAssembler;
  readonly #drawRanges: gfx.InputAssembler[] = [];
  readonly #nearest: gfx.Sampler;
  readonly #linear: gfx.Sampler;
  readonly #prepared: PreparedTextureV1[] = [];
  readonly #packer = new CaneCocosGeometryAssemblerV1();
  #surface: ColorTextureV1 | null = null;
  #space: CaneCocosBlendColorSpaceV1;
  #vertexCapacity = 112;
  #indexCapacity = 12;
  #destroyed = false;

  constructor(space = CaneCocosBlendColorSpaceV1.Gamma) {
    const device = director.root?.device;
    if (device === undefined) throw new Error("Cocos GFX device is unavailable.");
    const needed = gfx.FormatFeatureBit.RENDER_TARGET | gfx.FormatFeatureBit.SAMPLED_TEXTURE | gfx.FormatFeatureBit.LINEAR_FILTER;
    const format = [gfx.Format.RGBA16F, gfx.Format.RGBA32F].find(candidate => (device.getFormatFeatures(candidate) & needed) === needed);
    if (format === undefined) {
      throw new CaneCocosErrorV1("unsupportedBackend", "Cane color projection requires filterable floating-point render targets.", {
        operation: "cocosCreateColorSurface", field: "RGBA16F/RGBA32F", backend: String(device.gfxAPI),
      });
    }
    this.#device = device;
    this.#format = format;
    this.#space = space;
    this.#pass = device.createRenderPass(new gfx.RenderPassInfo([
      new gfx.ColorAttachment(format, gfx.SampleCount.X1, gfx.LoadOp.CLEAR, gfx.StoreOp.STORE),
    ]));
    this.#layout = device.createDescriptorSetLayout(new gfx.DescriptorSetLayoutInfo([
      new gfx.DescriptorSetLayoutBinding(0, gfx.DescriptorType.SAMPLER_TEXTURE, 1, gfx.ShaderStageFlagBit.FRAGMENT),
    ]));
    this.#pipelineLayout = device.createPipelineLayout(new gfx.PipelineLayoutInfo([this.#layout]));
    this.#shader = createShaderV1(device, false);
    this.#normalizeShader = createShaderV1(device, true);
    this.#command = device.createCommandBuffer(new gfx.CommandBufferInfo(device.queue));
    this.#vertices = device.createBuffer(new gfx.BufferInfo(gfx.BufferUsageBit.VERTEX | gfx.BufferUsageBit.TRANSFER_DST,
      gfx.MemoryUsageBit.HOST | gfx.MemoryUsageBit.DEVICE, this.#vertexCapacity, 28));
    this.#indices = device.createBuffer(new gfx.BufferInfo(gfx.BufferUsageBit.INDEX | gfx.BufferUsageBit.TRANSFER_DST,
      gfx.MemoryUsageBit.HOST | gfx.MemoryUsageBit.DEVICE, this.#indexCapacity, 2));
    this.#ia = device.createInputAssembler(new gfx.InputAssemblerInfo(attributesV1(), [this.#vertices], this.#indices));
    this.#nearest = device.getSampler(new gfx.SamplerInfo(gfx.Filter.POINT, gfx.Filter.POINT, gfx.Filter.NONE,
      gfx.Address.CLAMP, gfx.Address.CLAMP, gfx.Address.CLAMP));
    this.#linear = device.getSampler(new gfx.SamplerInfo(gfx.Filter.LINEAR, gfx.Filter.LINEAR, gfx.Filter.NONE,
      gfx.Address.CLAMP, gfx.Address.CLAMP, gfx.Address.CLAMP));
  }

  setColorSpace(space: CaneCocosBlendColorSpaceV1): void {
    if (this.#space === space) return;
    this.#space = space;
    this.#clearTextures();
  }

  /** Converted textures retain original dimensions, orientation and sampling policy. */
  prepare(source: Texture2D, description: RuntimeTextureV1): Texture2D {
    const raw = source.getGFXTexture();
    if (raw === null) throw new Error("Cane source texture is not uploaded.");
    const key = `${description.colorSpace}|${description.alphaMode}`;
    const existing = this.#prepared.find(entry => entry.source === source && entry.gfxSource === raw && entry.key === key);
    if (existing !== undefined) return existing.texture;
    // The source store replaces its Texture2D after context restoration/resource edits.
    for (let i = this.#prepared.length - 1; i >= 0; --i) {
      const old = this.#prepared[i]!;
      if (!old.source.isValid || (old.source === source && old.gfxSource !== raw)) {
        old.descriptor.destroy(); old.texture.destroy(); this.#prepared.splice(i, 1);
      }
    }
    const texture = new ColorTextureV1(this.#device, this.#pass, raw.width, raw.height, this.#sourceSampler(source), this.#format);
    const input = this.#descriptor(raw, this.#nearest);
    const flags = caneCocosColorFlagsV1(description, false, this.#space)
      | (raw.format === gfx.Format.SRGB8_A8 || raw.format === gfx.Format.SRGB8 ? 2 : 0);
    const quad = quadV1(-1, -1, 1, 1, this.#device.capabilities.screenSpaceSignY > 0 ? 0 : 1, flags);
    this.#upload(quad, QUAD_INDICES_V1);
    this.#begin(texture);
    this.#command.bindPipelineState(this.#state("normalize"));
    this.#command.bindDescriptorSet(0, input);
    this.#command.bindInputAssembler(this.#ia);
    this.#ia.indexCount = 6;
    this.#ia.firstIndex = 0;
    this.#command.draw(this.#ia);
    this.#finish();
    input.destroy();
    const descriptor = this.#descriptor(texture.getGFXTexture(), texture.getGFXSampler());
    this.#prepared.push({ source, gfxSource: raw, key, texture, descriptor });
    return texture;
  }

  render(packet: RuntimeRenderPacketV1, textures: CocosTextureStore,
    modulation: CaneCocosColorModulationV1): CaneCocosSurfaceFrameV1 | null {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const attachment of packet.attachments) {
      for (let i = 0; i < attachment.worldVerticesXy.length; i += 2) {
        const x = attachment.worldVerticesXy[i]!, y = attachment.worldVerticesXy[i + 1]!;
        minX = Math.min(minX, x); minY = Math.min(minY, y);
        maxX = Math.max(maxX, x); maxY = Math.max(maxY, y);
      }
      this.prepare(textures.texture(attachment.texture), attachment.texture);
    }
    if (!Number.isFinite(minX)) return null;
    minX = Math.floor(minX) - 1; minY = Math.floor(minY) - 1;
    maxX = Math.ceil(maxX) + 1; maxY = Math.ceil(maxY) + 1;
    const width = Math.max(1, maxX - minX), height = Math.max(1, maxY - minY);
    // Stable round-up allocations avoid texture churn while animated bounds move.
    const scale = Math.min(1, 2048 / Math.max(width, height));
    const targetWidth = Math.max(16, Math.ceil(width * scale / 16) * 16);
    const targetHeight = Math.max(16, Math.ceil(height * scale / 16) * 16);
    if (this.#surface === null || this.#surface.width < targetWidth || this.#surface.height < targetHeight) {
      const allocatedWidth = Math.max(targetWidth, this.#surface?.width ?? 0);
      const allocatedHeight = Math.max(targetHeight, this.#surface?.height ?? 0);
      this.#surface?.destroy();
      this.#surface = new ColorTextureV1(this.#device, this.#pass, allocatedWidth, allocatedHeight, this.#linear, this.#format);
    }
    const upload = this.#packer.applyRange(packet, 0, packet.attachments.length, modulation, null, this.#space, true);
    if (upload.vertexCount > 65535) throw new Error("Cocos isolated packet exceeds the 16-bit mesh limit.");
    for (let i = 0; i < upload.vertexCount; i++) {
      const offset = i * 7;
      upload.vertexData[offset] = (upload.vertexData[offset]! - minX) / width * 2 - 1;
      upload.vertexData[offset + 1] = (upload.vertexData[offset + 1]! - minY) / height * 2 - 1;
    }
    this.#upload(upload.vertexBytes, upload.indices);
    this.#begin(this.#surface);
    let firstIndex = 0;
    for (let index = 0; index < packet.attachments.length; index++) {
      const attachment = packet.attachments[index]!;
      const raw = textures.texture(attachment.texture);
      const entry = this.#prepared.find(item => item.source === raw
        && item.key === `${attachment.texture.colorSpace}|${attachment.texture.alphaMode}`)!;
      this.#command.bindPipelineState(this.#state(attachment.blendMode));
      this.#command.bindDescriptorSet(0, entry.descriptor);
      // The 3.8.8 WebGPU driver requires an InputAssembler (a bare DrawInfo is
      // treated as a non-indexed draw). It also defers reading its range until
      // endRenderPass, so each draw needs a distinct retained range object.
      const range = this.#drawRanges[index] ??= this.#device.createInputAssembler(
        new gfx.InputAssemblerInfo(attributesV1(), [this.#vertices], this.#indices),
      );
      range.indexCount = attachment.indices.length;
      range.firstIndex = firstIndex;
      this.#command.bindInputAssembler(range);
      this.#command.draw(range);
      firstIndex += attachment.indices.length;
    }
    this.#finish();
    return { texture: this.#surface, minX, minY, maxX, maxY,
      bottomV: this.#device.capabilities.screenSpaceSignY > 0 ? 0 : 1 };
  }

  destroy(): void {
    if (this.#destroyed) return;
    this.#clearTextures();
    this.#surface?.destroy(); this.#surface = null;
    for (const state of this.#states.values()) state.destroy();
    this.#states.clear(); this.#ia.destroy();
    for (const range of this.#drawRanges) range.destroy();
    this.#drawRanges.length = 0;
    this.#vertices.destroy(); this.#indices.destroy();
    this.#shader.destroy(); this.#normalizeShader.destroy(); this.#pipelineLayout.destroy();
    this.#layout.destroy(); this.#pass.destroy(); this.#command.destroy(); this.#destroyed = true;
  }

  #clearTextures(): void {
    for (const entry of this.#prepared) { entry.descriptor.destroy(); entry.texture.destroy(); }
    this.#prepared.length = 0;
  }

  #sourceSampler(source: Texture2D): gfx.Sampler {
    const info = new gfx.SamplerInfo().copy(source.getGFXSampler().info);
    // Derived color textures are one-level projections, even if a host texture has mips.
    info.mipFilter = gfx.Filter.NONE;
    return this.#device.getSampler(info);
  }

  #descriptor(texture: gfx.Texture, sampler: gfx.Sampler): gfx.DescriptorSet {
    const result = this.#device.createDescriptorSet(new gfx.DescriptorSetInfo(this.#layout));
    result.bindTexture(0, texture); result.bindSampler(0, sampler); result.update();
    return result;
  }

  #upload(vertices: ArrayBufferView, indices: Uint16Array | Uint32Array): void {
    if (vertices.byteLength > this.#vertexCapacity) {
      this.#vertexCapacity = vertices.byteLength; this.#vertices.resize(this.#vertexCapacity);
    }
    if (indices.byteLength > this.#indexCapacity) {
      this.#indexCapacity = indices.byteLength; this.#indices.resize(this.#indexCapacity);
    }
    // Creator declarations omit ArrayBufferView although every GFX backend accepts typed views.
    if (vertices.byteLength > 0) this.#vertices.update(vertices as unknown as ArrayBuffer);
    if (indices.byteLength > 0) this.#indices.update(indices as unknown as ArrayBuffer);
  }

  #begin(target: ColorTextureV1): void {
    this.#command.begin();
    this.#command.beginRenderPass(this.#pass, target.framebuffer, new gfx.Rect(0, 0, target.width, target.height),
      [new gfx.Color(0, 0, 0, 0)], 1, 0);
  }
  #finish(): void {
    this.#command.endRenderPass(); this.#command.end();
    // Native records commands on the game thread and requires a flush to its
    // rendering thread before submission. Web drivers implement this as a no-op.
    this.#device.flushCommands([this.#command]);
    this.#device.queue.submit([this.#command]);
  }
  #state(mode: RuntimeBlendModeV1 | "normalize"): gfx.PipelineState {
    const previous = this.#states.get(mode);
    if (previous !== undefined) return previous;
    const blend = new gfx.BlendState();
    if (mode !== "normalize") {
      const [src, dst] = blendFactorsV1(mode, true), [srcA, dstA] = blendAlphaFactorsV1(mode);
      const target = new gfx.BlendTarget();
      target.blend = true;
      target.blendSrc = src; target.blendDst = dst; target.blendEq = gfx.BlendOp.ADD;
      target.blendSrcAlpha = srcA; target.blendDstAlpha = dstA; target.blendAlphaEq = gfx.BlendOp.ADD;
      blend.setTarget(0, target);
    }
    const raster = new gfx.RasterizerState(); raster.cullMode = gfx.CullMode.NONE;
    const depth = new gfx.DepthStencilState(); depth.depthTest = false; depth.depthWrite = false;
    // JSB exports native state wrappers whose constructors do not accept the
    // positional parameters supported by the web TypeScript implementations.
    const info = new gfx.PipelineStateInfo();
    info.shader = mode === "normalize" ? this.#normalizeShader : this.#shader;
    info.pipelineLayout = this.#pipelineLayout; info.renderPass = this.#pass;
    info.inputState = new gfx.InputState(attributesV1()); info.rasterizerState = raster;
    info.depthStencilState = depth; info.blendState = blend;
    const result = this.#device.createPipelineState(info);
    this.#states.set(mode, result); return result;
  }
}

const QUAD_INDICES_V1 = new Uint16Array([0, 1, 2, 0, 2, 3]);

export function quadV1(minX: number, minY: number, maxX: number, maxY: number, bottomV: number, flags: number): Uint8Array {
  const values = new Float32Array([minX, minY, 0, 0, bottomV, 0, 0,
    maxX, minY, 0, 1, bottomV, 0, 0, maxX, maxY, 0, 1, 1 - bottomV, 0, 0,
    minX, maxY, 0, 0, 1 - bottomV, 0, 0]);
  const bytes = new Uint8Array(values.buffer);
  for (let i = 0; i < 4; i++) { bytes.fill(255, i * 28 + 20, i * 28 + 24); bytes[i * 28 + 27] = flags; }
  return bytes;
}

function attributesV1(): gfx.Attribute[] {
  return [new gfx.Attribute("a_position", gfx.Format.RGB32F, false, 0, false, 0),
    new gfx.Attribute("a_texCoord", gfx.Format.RG32F, false, 0, false, 1),
    new gfx.Attribute("a_color", gfx.Format.RGBA8, true, 0, false, 2),
    new gfx.Attribute("a_color2", gfx.Format.RGBA8, true, 0, false, 3)];
}

function createShaderV1(device: gfx.Device, normalize: boolean): gfx.Shader {
  const legacy = device.gfxAPI === gfx.API.WEBGL || device.gfxAPI === gfx.API.GLES2;
  const gles = device.gfxAPI === gfx.API.WEBGL2 || device.gfxAPI === gfx.API.GLES3;
  const modern = !legacy && !gles;
  const header = legacy || gles || device.gfxAPI === gfx.API.WEBGPU ? "" : "#version 450\n";
  const attrib = (name: string, type: string, location: number) => `${modern || gles ? `layout(location=${location}) ` : ""}${legacy ? "attribute" : "in"} ${type} ${name};`;
  const vary = (direction: string) => `${legacy ? "varying" : direction} vec2 uv0;\n${legacy ? "varying" : direction} vec4 v_light;\n${legacy ? "varying" : direction} vec4 v_dark;`;
  // For explicit-layout APIs each vary requires its own location.
  const varying = (direction: string) => modern
    ? `layout(location=0) ${direction} vec2 uv0;\nlayout(location=1) ${direction} vec4 v_light;\nlayout(location=2) ${direction} vec4 v_dark;`
    : vary(direction);
  const vertex = `${header}precision highp float;\n${attrib("a_position", "vec3", 0)}\n${attrib("a_texCoord", "vec2", 1)}\n${attrib("a_color", "vec4", 2)}\n${attrib("a_color2", "vec4", 3)}\n${varying("out")}\nvoid main(){gl_Position=vec4(a_position,1.0);uv0=a_texCoord;v_light=a_color;v_dark=a_color2;}`;
  let body = CANE_COCOS_FRAGMENT_V1.replace("__CANE_SAMPLE__", `${legacy ? "texture2D" : "texture"}(cc_spriteTexture,uv0)`);
  if (normalize) {
    const begin = body.indexOf("vec4 frag ()");
    body = body.slice(0, begin) + `vec4 frag () {
      vec4 texel = ${legacy ? "texture2D" : "texture"}(cc_spriteTexture,uv0);
      float flags = floor(v_dark.a*255.0+0.5);
      float a = clamp(texel.a,0.0,1.0);
      vec3 linear = caneColorFlagV2(flags,2.0) ? texel.rgb : caneDecodeSrgbV1(texel.rgb);
      if(caneColorFlagV2(flags,4.0)) linear = a>0.000001 ? linear/a : vec3(0.0);
      linear = clamp(linear,vec3(0.0),vec3(1.0));
      vec3 work = caneColorFlagV2(flags,8.0) ? caneEncodeSrgbV1(linear) : linear;
      return vec4(caneColorFlagV2(flags,4.0) ? work*a : work,a);
    }`;
  }
  // Creator 3.8.8 splits combined WebGPU samplers with a whitespace-sensitive
  // GLSL parser. Keep its canonical layout spelling so texture and sampler get
  // distinct bindings before SPIR-V/WGSL conversion.
  const fragment = `${header}precision highp float;\n#define TWO_COLORED 1\n#define ALPHA_TEST(value)\n${varying("in")}\n${modern ? "layout(set = 0, binding = 0) " : ""}uniform sampler2D cc_spriteTexture;\n${legacy ? "" : "layout(location=0) out vec4 caneOutput;"}\n${body}\nvoid main(){${legacy ? "gl_FragColor" : "caneOutput"}=frag();}`;
  const info = new gfx.ShaderInfo(`CaneColorSurface:${normalize ? "normalize" : "compose"}`,
    [new gfx.ShaderStage(gfx.ShaderStageFlagBit.VERTEX, vertex), new gfx.ShaderStage(gfx.ShaderStageFlagBit.FRAGMENT, fragment)],
    attributesV1(), [], [], [new gfx.UniformSamplerTexture(0, 0, "cc_spriteTexture", gfx.Type.SAMPLER2D, 1)]);
  let hash = 0x811c9dc5;
  for (const character of vertex + fragment) hash = Math.imul(hash ^ character.charCodeAt(0), 0x01000193);
  info.hash = hash >>> 0;
  return device.createShader(info);
}
