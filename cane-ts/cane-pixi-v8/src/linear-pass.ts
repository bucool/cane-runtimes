import {
  Geometry, Rectangle, RenderTexture, RendererType, Shader, State, UniformGroup,
  type Renderer, type RenderSurface, type WebGLRenderer, type WebGPURenderer, type TEXTURE_FORMATS, Texture,
} from "pixi.js";
import { CaneLinearGpuPipelineV1 } from "./linear-gpu-pipeline.js";

/**
 * A Linear island includes the existing destination, not just Cane source RGB.
 * Ordered batch draws still use hardware blending and retain triangle overlap.
 */
export class CanePixiLinearPassV1 {
  readonly #renderer: WebGLRenderer | WebGPURenderer;
  readonly #geometry = new Geometry({
    attributes: { aPosition: { buffer: new Float32Array([0,0, 1,0, 1,1, 0,1]), format: "float32x2" } },
    indexBuffer: new Uint32Array([0,1,2, 0,2,3]),
  });
  readonly #state = new State();
  readonly #shaders = new Map<number, Shader>();
  readonly #gpuPipeline: CaneLinearGpuPipelineV1 | null;
  #snapshot: RenderTexture | null = null;
  #work: RenderTexture | null = null;
  readonly #targets = new Map<string, { snapshot: RenderTexture; work: RenderTexture; tick: number }>();
  #destination: RenderSurface | null = null;
  #uniformsPushed = false;
  #frame = new Rectangle();
  #mip = 0;
  #layer = 0;

  constructor(renderer: Renderer) {
    this.#renderer = renderer as WebGLRenderer | WebGPURenderer;
    this.#gpuPipeline = renderer.type === RendererType.WEBGPU ? new CaneLinearGpuPipelineV1(renderer as WebGPURenderer) : null;
    this.#state.blend = false;
  }

  #transferShader(encode: boolean, root: boolean, flipSource: boolean): Shader {
    const key = (encode ? 1 : 0) | (root ? 2 : 0) | (flipSource ? 4 : 0);
    const existing = this.#shaders.get(key);
    if (existing) return existing;
    // Separate immutable uniform buffers: WebGPU executes both transfers after
    // submission, so reusing one mutable buffer would encode the backdrop twice.
    const parameters = new UniformGroup({ uParameters: {
      value: new Float32Array([encode ? 1 : 0, root ? -1 : 1, flipSource ? 1 : 0, 0]), type: "vec4<f32>",
    } });
    const shader = Shader.from({
      gl: { vertex: BLIT_VERTEX, fragment: BLIT_FRAGMENT, preferredFragmentPrecision: "highp" },
      gpu: { vertex: { source: BLIT_WGSL, entryPoint: "vertexMain" }, fragment: { source: BLIT_WGSL, entryPoint: "fragmentMain" } },
      resources: { parameters, uSource: Texture.EMPTY.source, uSampler: Texture.EMPTY.source.style },
    });
    this.#shaders.set(key, shader);
    return shader;
  }

  begin(): void {
    if (this.#destination !== null) throw new Error("Cane Linear render pass cannot be nested.");
    const renderer = this.#renderer;
    const rt = renderer.renderTarget;
    if (renderer.type === RendererType.WEBGL && !(renderer as WebGLRenderer).gl.getExtension("EXT_color_buffer_float")) {
      throw new Error("Cane Linear blending requires WebGL2 floating-point render targets (EXT_color_buffer_float), or WebGPU.");
    }
    if (rt.mipLevel !== 0 || rt.layer !== 0) {
      throw new Error("Cane Linear blending requires a base-level 2D render target.");
    }
    const target = rt.renderTarget;
    if (target.colorTextures.length !== 1 || target.stencil || target.depth || target.depthStencilTexture) {
      throw new Error("Cane Linear blending requires one color attachment without depth/stencil masks.");
    }
    if (target.colorTexture.format !== "rgba8unorm" && target.colorTexture.format !== "bgra8unorm") {
      throw new Error("Cane Linear blending requires an sRGB-encoded rgba8unorm/bgra8unorm presentation target.");
    }
    if (renderer.type === RendererType.WEBGL) {
      const gl = (renderer as WebGLRenderer).gl;
      if (gl.isEnabled(gl.STENCIL_TEST) || gl.isEnabled(gl.SCISSOR_TEST)
        || !(gl.getParameter(gl.COLOR_WRITEMASK) as boolean[]).every(Boolean)) {
        throw new Error("Cane Linear blending does not support an active host stencil, scissor or color-write mask.");
      }
    }
    this.#ensureTargets(target.width, target.height, target.resolution, target.colorTexture.format);
    this.#destination = rt.renderSurface;
    this.#mip = rt.mipLevel;
    this.#layer = rt.layer;
    this.#frame.copyFrom(rt.viewport);
    const resolution = target.resolution;
    this.#frame.x /= resolution; this.#frame.y /= resolution;
    this.#frame.width /= resolution; this.#frame.height /= resolution;
    const snapshot = this.#snapshot!;
    const work = this.#work!;
    try {
      this.#gpuPipeline?.begin();
      rt.finishRenderPass();
      rt.copyToTexture(target, snapshot, { x: 0, y: 0 }, { width: target.pixelWidth, height: target.pixelHeight }, { x: 0, y: 0 });
      rt.bind(work, true);
      this.#blit(snapshot, false, renderer.type === RendererType.WEBGL && target.isRoot);
      // Preserve the original viewport, transform, filter offset and group tint.
      rt.bind(work, false, undefined, this.#frame);
      renderer.globalUniforms.push({});
      this.#uniformsPushed = true;
    } catch (error) {
      this.#restore();
      throw error;
    }
  }

  end(): void {
    const destination = this.#destination;
    if (destination === null || this.#work === null) return;
    const renderer = this.#renderer;
    const rt = renderer.renderTarget;
    try {
      rt.finishRenderPass();
      // Full target contains an exact copy of the backdrop outside Cane draws.
      // Bind the whole source, even if the original surface was a subtexture.
      rt.bind(rt.getRenderTarget(destination), false);
      this.#blit(this.#work, true, false);
    } finally {
      this.#restore();
    }
  }

  #restore(): void {
    try {
      if (this.#uniformsPushed) {
        this.#uniformsPushed = false;
        this.#renderer.globalUniforms.pop();
      }
    } finally {
      try {
        if (this.#destination !== null) this.#renderer.renderTarget.bind(this.#destination, false, undefined, this.#frame, this.#mip, this.#layer);
      } finally {
        this.#destination = null;
        this.#gpuPipeline?.end();
      }
    }
  }

  #blit(source: RenderTexture, encode: boolean, flipSource: boolean): void {
    const shader = this.#transferShader(encode, this.#renderer.renderTarget.renderTarget.isRoot, flipSource);
    shader.resources.uSource = source.source;
    shader.resources.uSampler = source.source.style;
    this.#renderer.encoder.draw({ geometry: this.#geometry, shader, state: this.#state });
  }

  #ensureTargets(width: number, height: number, resolution: number, format: TEXTURE_FORMATS): void {
    const tick = this.#renderer.tick;
    // Never destroy resources referenced by unsubmitted draws in this frame.
    // Drop inactive sizes on a later frame instead of growing a permanent pool.
    for (const [key, value] of this.#targets) {
      if (value.tick < tick - 1) {
        for (const shader of this.#shaders.values()) {
          if (shader.resources.uSource === value.snapshot.source || shader.resources.uSource === value.work.source) {
            shader.resources.uSource = Texture.EMPTY.source;
            shader.resources.uSampler = Texture.EMPTY.source.style;
          }
        }
        value.snapshot.destroy(true); value.work.destroy(true); this.#targets.delete(key);
      }
    }
    const key = [width, height, resolution, format].join("/");
    const cached = this.#targets.get(key);
    if (cached) {
      cached.tick = tick; this.#snapshot = cached.snapshot; this.#work = cached.work; return;
    }
    const options = { width, height, resolution, scaleMode: "nearest" as const, antialias: false };
    this.#snapshot = RenderTexture.create({ ...options, label: "Cane Gamma backdrop", format });
    this.#work = RenderTexture.create({ ...options, label: "Cane Linear composition", format: "rgba16float" });
    this.#targets.set(key, { snapshot: this.#snapshot, work: this.#work, tick });
  }

  destroy(): void {
    for (const value of this.#targets.values()) { value.snapshot.destroy(true); value.work.destroy(true); }
    this.#targets.clear();
    for (const shader of this.#shaders.values()) shader.destroy();
    this.#shaders.clear(); this.#geometry.destroy();
    this.#gpuPipeline?.destroy();
  }
}

const BLIT_VERTEX = `
in vec2 aPosition; out vec2 vUv; uniform vec4 uParameters;
void main() {
  gl_Position = vec4(aPosition.x * 2.0 - 1.0, (aPosition.y * 2.0 - 1.0) * uParameters.y, 0.0, 1.0);
  vUv = vec2(aPosition.x, uParameters.z > .5 ? 1.0 - aPosition.y : aPosition.y);
}`;
const BLIT_FRAGMENT = `
in vec2 vUv; out vec4 finalColor; uniform sampler2D uSource; uniform vec4 uParameters;
float transfer(float c) {
  if (uParameters.x > .5) return c <= .0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - .055;
  return c <= .04045 ? c / 12.92 : pow((c + .055) / 1.055, 2.4);
}
void main() {
  vec4 c = texture(uSource, vUv);
  float a = clamp(c.a, 0.0, 1.0);
  vec3 rgb = a > .000001 ? c.rgb / a : vec3(0.0);
  finalColor = vec4(vec3(transfer(rgb.r), transfer(rgb.g), transfer(rgb.b)) * a, c.a);
}`;
const BLIT_WGSL = `
struct Parameters { uParameters: vec4<f32> };
@group(0) @binding(0) var<uniform> parameters: Parameters;
@group(0) @binding(1) var uSource: texture_2d<f32>;
@group(0) @binding(2) var uSampler: sampler;
struct VertexOutput { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32> };
@vertex fn vertexMain(@location(0) aPosition: vec2<f32>) -> VertexOutput {
  var output: VertexOutput;
  output.position = vec4<f32>(aPosition.x * 2.0 - 1.0, (aPosition.y * 2.0 - 1.0) * parameters.uParameters.y, 0.0, 1.0);
  output.uv = vec2<f32>(aPosition.x, select(aPosition.y, 1.0 - aPosition.y, parameters.uParameters.z > .5));
  return output;
}
fn transfer(c: f32) -> f32 {
  if (parameters.uParameters.x > .5) {
    if (c <= .0031308) { return c * 12.92; }
    return 1.055 * pow(c, 1.0 / 2.4) - .055;
  }
  if (c <= .04045) { return c / 12.92; }
  return pow((c + .055) / 1.055, 2.4);
}
@fragment fn fragmentMain(input: VertexOutput) -> @location(0) vec4<f32> {
  let c = textureSample(uSource, uSampler, input.uv);
  let a = clamp(c.a, 0.0, 1.0);
  var rgb = vec3<f32>(0.0);
  if (a > .000001) { rgb = c.rgb / a; }
  return vec4<f32>(vec3<f32>(transfer(rgb.r), transfer(rgb.g), transfer(rgb.b)) * a, c.a);
}`;
