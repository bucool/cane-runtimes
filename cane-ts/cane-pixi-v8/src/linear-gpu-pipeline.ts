import type { Geometry, GpuProgram, State, Topology, WebGPURenderer } from "pixi.js";

/**
 * Pixi 8.18.1's stock WebGPU pipeline fixes every color target to bgra8unorm.
 * During Cane's bounded composition pass, build pipelines for the actual target.
 * Resource layouts, buffer binding and draw ordering still belong to Pixi.
 */
export class CaneLinearGpuPipelineV1 {
  readonly #renderer: WebGPURenderer;
  #restore: (() => void) | null = null;
  #device: GPUDevice | null = null;
  #pipelines = new Map<string, GPURenderPipeline>();
  #modules = new Map<string, GPUShaderModule>();

  constructor(renderer: WebGPURenderer) { this.#renderer = renderer; }

  begin(): void {
    const system = this.#renderer.pipeline;
    const original = system.getPipeline;
    if (this.#restore !== null) throw new Error("Cane GPU composition is already active.");
    if (this.#device !== this.#renderer.gpu.device) {
      this.#device = this.#renderer.gpu.device;
      this.#pipelines.clear(); this.#modules.clear();
    }
    system.getPipeline = (geometry, program, state, topology) => {
      // Prime the native buffer-layout/name cache without using its color pipeline.
      original.call(system, geometry, program, state, topology);
      return this.#getPipeline(geometry, program, state, topology ?? geometry.topology);
    };
    this.#restore = () => { system.getPipeline = original; };
  }

  end(): void { this.#restore?.(); this.#restore = null; }
  destroy(): void { this.end(); this.#pipelines.clear(); this.#modules.clear(); }

  #getPipeline(geometry: Geometry, program: GpuProgram, state: State, topology: Topology): GPURenderPipeline {
    const renderer = this.#renderer;
    const target = renderer.renderTarget.renderTarget;
    const gpuTarget = renderer.renderTarget.getGpuRenderTarget(target);
    const formats = target.colorTextures.map(texture => texture.source.format as GPUTextureFormat);
    const key = [geometry.uid, program._layoutKey, state.data, state.blendMode, topology, gpuTarget.msaaSamples, ...formats].join("/");
    if (gpuTarget.descriptor.depthStencilAttachment) {
      throw new Error("Cane Linear WebGPU composition does not support a depth/stencil destination.");
    }
    const existing = this.#pipelines.get(key);
    if (existing) return existing;
    const buffers: GPUVertexBufferLayout[] = [];
    for (const buffer of geometry.buffers) {
      const attributes: GPUVertexAttribute[] = [];
      let stride = 0, instance = false;
      for (const [name, input] of Object.entries(program.attributeData)) {
        const attribute = geometry.attributes[name]!;
        if (attribute.buffer !== buffer) continue;
        stride = attribute.stride!; instance = attribute.instance ?? false;
        attributes.push({ shaderLocation: input.location!, format: attribute.format as GPUVertexFormat, offset: attribute.offset ?? 0 });
      }
      if (attributes.length) buffers.push({ arrayStride: stride, stepMode: instance ? "instance" : "vertex", attributes });
    }
    const blend = renderer.state.getColorTargets(state, formats.length);
    const targets: GPUColorTargetState[] = formats.map((format, index) => ({
      format, writeMask: GPUColorWrite.ALL,
      ...(state.blend ? { blend: blend[index]!.blend! } : {}),
    }));
    const pipeline = this.#device!.createRenderPipeline({
      label: "Cane selected-space composition",
      layout: renderer.shader.getProgramData(program).pipeline,
      vertex: { module: this.#module(program.vertex!.source), entryPoint: program.vertex!.entryPoint ?? "main", buffers },
      fragment: { module: this.#module(program.fragment!.source), entryPoint: program.fragment!.entryPoint ?? "main", targets },
      primitive: { topology, cullMode: state.cullMode },
      multisample: { count: gpuTarget.msaaSamples },
    });
    this.#pipelines.set(key, pipeline);
    return pipeline;
  }

  #module(source: string): GPUShaderModule {
    let module = this.#modules.get(source);
    if (!module) { module = this.#device!.createShaderModule({ code: source }); this.#modules.set(source, module); }
    return module;
  }
}
