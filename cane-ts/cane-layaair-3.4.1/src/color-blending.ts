import { assertCaneLayaWebRendererV1 } from "./engine.js";
import { CaneLayaErrorV1 } from "./errors.js";

export type CaneLayaColorBlendingV1 = "gamma" | "linear";

/**
 * Selects the working space for the complete Laya 2D stage. Gamma is native
 * canvas composition; Linear renders to a linear target and encodes only the
 * finished image. A shared destination cannot have a different space per actor.
 */
export function setCaneLayaColorBlendingV1(
  value: CaneLayaColorBlendingV1,
): CaneLayaColorPipelineV1 {
  assertCaneLayaWebRendererV1("layaSetColorBlending");
  const pipeline = Laya.stage.getComponent(CaneLayaColorPipelineV1)
    ?? Laya.stage.addComponent(CaneLayaColorPipelineV1);
  pipeline.colorBlending = value;
  return pipeline;
}

/** Stage-owned renderer option. Destroying/disabling it restores native Gamma. */
export class CaneLayaColorPipelineV1 extends Laya.Component {
  #colorBlending: CaneLayaColorBlendingV1 = "gamma";
  #target: Laya.RenderTexture2D | null = null;
  #commands: Laya.CommandBuffer2D | null = null;
  #presentation: Laya.Material | null = null;
  #savedPass: { target: Laya.RenderTexture2D; clear: boolean; offset: Laya.Matrix } | null = null;
  readonly #changedTextures = new Map<InternalColorTextureV1, number>();

  get colorBlending(): CaneLayaColorBlendingV1 { return this.#colorBlending; }

  set colorBlending(value: CaneLayaColorBlendingV1) {
    if (value !== "gamma" && value !== "linear") {
      throw new CaneLayaErrorV1("invalidArgument", "colorBlending must be gamma or linear.", {
        operation: "layaSetColorBlending", field: "colorBlending", actual: value,
      });
    }
    if (value === this.#colorBlending) return;
    assertCaneLayaWebRendererV1("layaSetColorBlending");
    if (this.owner !== Laya.stage) throw new Error("CaneLayaColorPipelineV1 must be attached to Laya.stage.");
    if (value === "linear") this.#assertStageSupported();
    this.#restorePass();
    this.#colorBlending = value;
    // Force retained/cached 2D passes to redraw after their working space changes.
    for (const pass of stagePassesV1()) pass.repaint = true;
    Laya.stage.repaint();
  }

  override onPreRender(): void {
    if (this.#colorBlending !== "linear") return;
    this.#assertStageSupported();
    const pass = Laya.stage.passManager.basePass;
    if (this.#savedPass === null) {
      if (pass.renderTexture != null) {
        throw new Error("Cane Linear stage composition requires the stage base pass to target the canvas.");
      }
      this.#savedPass = { target: pass.renderTexture, clear: pass.doClearColor, offset: pass.offsetMatrix.clone() };
    }
    const width = Laya.RenderState2D.width;
    const height = Laya.RenderState2D.height;
    if (width <= 0 || height <= 0) return;
    if (this.#target === null || this.#target.width !== width || this.#target.height !== height) {
      this.#commands?.clear();
      if (this.#target !== null) {
        const bridge = this.#target as unknown as ColorTargetBridgeV1;
        for (const texture of targetTexturesV1(bridge)) this.#changedTextures.delete(texture);
      }
      this.#target?.destroy();
      this.#target = new Laya.RenderTexture2D(width, height,
        Laya.RenderTargetFormat.R16G16B16A16, Laya.RenderTargetFormat.DEPTHSTENCIL_24_8);
      this.#target.filterMode = Laya.FilterMode.Point;
      markTargetLinearV1(this.#target, this.#changedTextures);
      this.#preparePresentation();
    }
    pass.renderTexture = this.#target;
    pass.doClearColor = true;
    const color = Laya.ColorUtils.create(Laya.stage.bgColor).arrColor;
    const alpha = color[3] ?? 0;
    pass.setClearColor(decodeSrgbV1(color[0] ?? 0) * alpha,
      decodeSrgbV1(color[1] ?? 0) * alpha, decodeSrgbV1(color[2] ?? 0) * alpha, alpha);
    // Offscreen passes normally remove the root transform. The stage target
    // keeps the same physical-pixel layout as the native canvas path.
    pass.root.renderMatrix.copyTo(pass.offsetMatrix);
    pass.offsetMatrix.invert();
    pass.repaint = true;
    // Engine-owned masks/cache passes share the stage's working space. This is
    // applied after Laya prepares subpasses, before it draws any of them.
    for (const subpass of stagePassesV1()) {
      if (subpass.renderTexture != null && markTargetLinearV1(subpass.renderTexture, this.#changedTextures)) {
        subpass.repaint = true;
        refreshSubpassTextureV1(subpass);
      }
    }
  }

  override onPostRender(): void {
    if (this.#colorBlending === "linear" && this.#target !== null) {
      this.#commands?.apply(true, false);
    }
  }

  override onDisable(): void { this.#restorePass(); }

  override onDestroy(): void {
    this.#restorePass();
    this.#commands?.clear();
    (this.#commands as unknown as { shaderData: Laya.ShaderData } | null)?.shaderData.destroy();
    this.#commands = null;
    this.#presentation?.destroy();
    this.#presentation = null;
    this.#target?.destroy();
    this.#target = null;
  }

  #assertStageSupported(): void {
    // A 3D camera has its own tonemapping/output pipeline and must be integrated
    // by its host. Do not silently replace an existing 3D canvas destination.
    const stage = Laya.stage as unknown as { readonly _scene3Ds: readonly unknown[] };
    if (stage._scene3Ds.length > 0) {
      throw new Error("Cane Linear stage composition supports a 2D stage; integrate 3D camera output in a linear target first.");
    }
  }

  #preparePresentation(): void {
    if (this.#presentation === null) {
      installPresentationShaderV1();
      this.#presentation = new Laya.Material();
      this.#presentation.setShaderName(PRESENT_SHADER_V1);
    }
    this.#commands ??= new Laya.CommandBuffer2D("Cane Linear presentation");
    this.#commands.clear();
    // Laya 3.4.1 implements null as the canvas target; its declaration omits null.
    const canvas = null as unknown as Laya.IRenderTarget;
    this.#commands.blitTextureQuad(this.#target!, canvas, undefined,
      Laya.Shader3D.find(PRESENT_SHADER_V1), this.#presentation.shaderData);
  }

  #restorePass(): void {
    if (this.#savedPass !== null) {
      const pass = Laya.stage.passManager.basePass;
      pass.renderTexture = this.#savedPass.target;
      pass.doClearColor = this.#savedPass.clear;
      this.#savedPass.offset.copyTo(pass.offsetMatrix);
      pass.repaint = true;
      this.#savedPass = null;
    }
    for (const [texture, gamma] of this.#changedTextures) texture.gammaCorrection = gamma;
    this.#changedTextures.clear();
    for (const pass of stagePassesV1()) refreshSubpassTextureV1(pass);
  }
}

// These are exact 3.4.1 driver metadata, not texture pixel transformations.
// RenderTexture2D creates non-sRGB GPU storage but labels every target gamma=2.2;
// both web drivers use this field to select their GAMMASPACE shader variant.
interface InternalColorTextureV1 { gammaCorrection: number; readonly useSRGBLoad: boolean; }
interface ColorTargetBridgeV1 {
  readonly _texture: InternalColorTextureV1;
  readonly _renderTarget: {
    readonly _textures: readonly InternalColorTextureV1[];
    readonly _texturesResolve?: readonly InternalColorTextureV1[];
  };
}

function markTargetLinearV1(target: Laya.RenderTexture2D, saved: Map<InternalColorTextureV1, number>): boolean {
  const bridge = target as unknown as ColorTargetBridgeV1;
  let changed = false;
  for (const texture of targetTexturesV1(bridge)) {
    if (texture.useSRGBLoad) throw new Error("Cane Linear working target must use non-sRGB GPU storage.");
    if (texture.gammaCorrection === 1) continue;
    saved.set(texture, texture.gammaCorrection);
    texture.gammaCorrection = 1;
    changed = true;
  }
  return changed;
}

function targetTexturesV1(target: ColorTargetBridgeV1): InternalColorTextureV1[] {
  return [target._texture, ...target._renderTarget._textures, ...target._renderTarget._texturesResolve ?? []];
}

function stagePassesV1(): readonly Laya.IRender2DPass[] {
  const bridge = Laya.stage.passManager as unknown as { readonly _manager: { readonly _passes: Laya.IRender2DPass[] } };
  return bridge._manager._passes;
}

function refreshSubpassTextureV1(pass: Laya.IRender2DPass): void {
  // 3.4.1 retains GAMMATEXTURE in the cache/mask composite's primitive data.
  // Updating the target label alone leaves that sampler define stale.
  const owner = pass.root?.owner as unknown as {
    readonly _subStructRender?: {
      readonly _textureHost: Laya.RenderTexture2D | null;
      _updateTextureState(texture: Laya.RenderTexture2D | null): void;
    };
  } | undefined;
  const renderer = owner?._subStructRender;
  if (renderer != null) renderer._updateTextureState(renderer._textureHost);
}

function decodeSrgbV1(value: number): number {
  return value <= 0.04045 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
}

const PRESENT_SHADER_V1 = "CaneLinearPresentation_v1";

function installPresentationShaderV1(): void {
  if (Laya.Shader3D.find(PRESENT_SHADER_V1) != null) return;
  const shader = Laya.Shader3D.add(PRESENT_SHADER_V1, false, false);
  shader.shaderType = Laya.ShaderFeatureType.PostProcess;
  const subShader = new Laya.SubShader({ a_PositionTexcoord: [0, Laya.ShaderDataType.Vector4] }, {
    u_OffsetScale: Laya.ShaderDataType.Vector4,
    u_MainTex: Laya.ShaderDataType.Texture2D,
    u_MainTex_TexelSize: Laya.ShaderDataType.Vector4,
  });
  shader.addSubShader(subShader);
  const pass = subShader.addShaderPass(`
    varying vec2 v_uv;
    void main() { gl_Position = vec4(a_PositionTexcoord.xy, 0.0, 1.0); v_uv = a_PositionTexcoord.zw; }
  `, `
    #if defined(GL_FRAGMENT_PRECISION_HIGH)
    precision highp float;
    #else
    precision mediump float;
    #endif
    varying vec2 v_uv;
    float encode(float x) { return x <= 0.0031308 ? x * 12.92 : 1.055 * pow(x, 1.0 / 2.4) - 0.055; }
    void main() {
      vec4 color = texture2D(u_MainTex, v_uv);
      float coverage = clamp(color.a, 0.0, 1.0);
      vec3 straight = coverage > 0.000001 ? color.rgb / coverage : vec3(0.0);
      gl_FragColor = vec4(vec3(encode(straight.r), encode(straight.g), encode(straight.b)) * coverage, color.a);
    }
  `);
  pass.statefirst = true;
  pass.renderState.depthWrite = false;
  pass.renderState.depthTest = Laya.RenderState.DEPTHTEST_OFF;
  pass.renderState.cull = Laya.RenderState.CULL_NONE;
  pass.renderState.blend = Laya.RenderState.BLEND_DISABLE;
}
