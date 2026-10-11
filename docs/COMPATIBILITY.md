# Compatibility and version policy

This repository currently contains source candidates, not published npm
artifacts. Package version, Runtime API version, Runtime Format version, and
conformance suite version are independent compatibility domains.

## Current matrix

| Component | Current compatibility | Status |
| --- | --- | --- |
| `@cane-runtime/core` | ESM, ES2022, TypeScript declarations; Runtime JSON/CANEB 1.1, Atlas 1.0; Runtime API 1.0–1.13 | Suite 1.17.0, 391 required cases; passed twice from fresh state |
| `@cane-runtime/pixi-v8` | PixiJS 8.18.1 peer; browser ESM plus optional Core-inclusive IIFE | WebGL and WebGPU visually exercised; Canvas2D is an explicit downgrade |
| `@cane-runtime/layaair-3.4.1` | Exact LayaAir 3.4.1 peer; prebuilt ESM/IIFE/declarations and IDE resource package | JSON/CANEB import, serialized scene component and preview; WebGL/WebGPU game builds; native/NoRender rejected explicitly |
| `@cane-runtime/cocos-3.8.8` | Exact Cocos Creator 3.8.8 virtual `cc` module; ESM and declarations; no bundled engine | Creator WebGL/WebGPU plus Windows Native RenderEntity route |
| C# Core | `netstandard2.1`, Runtime JSON/CANEB 1.1, Atlas 1.0 and Runtime API 1.13 | Engine-neutral owned data, players and final packets |
| Unity | Unity 6000.3.23f1 and 6000.6.0f1 on Windows; Built-in, URP/HDRP integrations | Linear project color space; see the engine integration guide for tested pipeline versions and pass setup |
| C++ Core | C++17, Runtime JSON/CANEB 1.1, Atlas 1.0 and Runtime API 1.13 | Native engine-neutral Core |
| Godot | Ordinary Godot 4.7.2, Windows x64; Compatibility and Forward+; compiled against the 4.5 native ABI | Requires HDR 2D; does not require .NET. The ABI minimum does not certify older renderers |
| Unreal Engine | UE 5.8 / Windows x64; independent C++ Core | Runtime and editor import modules must link the same staged Core SDK; see the source installation guide |

The Pixi peer is intentionally exact while the adapter is pre-1.0. A wider
Pixi range requires its own WebGL, WebGPU, Canvas, lifecycle, and integration
test run. JavaScript engines that reuse `@cane-runtime/core` share the same
CPU animation/constraint/geometry result; renderer pixels may still vary with
backend sampling, color management, and shader precision.

The Pixi package provides two distribution shapes with identical Runtime
semantics:

- ESM modules for npm-aware bundlers, with `pixi.js` as a peer and Core as a
  package dependency;
- `dist/iife/cane-pixi-v8.min.js` for classic browser scripts, with Core
  embedded and `globalThis.PIXI` external. It exports
  `globalThis.CanePixi`, exposes the Core API as `CanePixi.Core`, and requires
  PixiJS to be loaded first.

The LayaAir package uses the same two shapes: ESM with `layaair` declared as
the exact `3.4.1` peer, and `dist/iife/cane-layaair-3.4.1.min.js` with Core
embedded and `globalThis.Laya` external. The public npm package named
`layaair` does not currently distribute 3.4.1, so development and consumers
must supply the exact official SDK/IDE engine rather than relaxing the peer.

## Whole-mesh placement

The TypeScript, C# and C++ cores support Runtime API 1.13
[`mesh.placement`](../specification/runtime/MESH_PLACEMENT_V1.md). Moving a
weighted image translates its final skinned geometry while preserving its
existing animation. JSON, CANEB, resource overlays, linked meshes and inverse
geometry queries use the same placement rules. The final packet layout is
unchanged; engine adapters must not apply a second offset.

PixiJS, LayaAir and Cocos use the TypeScript Core, Unity uses C# Core, and Godot
and Unreal use C++ Core. Upgrade the Core dependency or rebuild embedded bundles
and native libraries together with the adapter. Unreal's editor importer and
runtime module both require the updated staged library.

The extension has 150 JSON/CANEB executions per core in two fresh processes,
with cross-language geometry comparisons. It is verified separately from the
unchanged Suite 1.17.0 and its 391 cases; the suite identity does not certify the
extension or an engine's GPU backends. Files with zero or absent placement retain
their previous requirements; nonzero placement requires API 1.13 and the
`mesh.placement` required feature.

## PixiJS renderer capability matrix

| Capability | WebGL2 | WebGPU | Canvas2D |
| --- | :---: | :---: | :---: |
| Region/Mesh final geometry and UV | Yes | Yes | Yes |
| Core CPU clipping and Slot-object masks | Yes | Yes | Yes |
| Full affine, shear, reflection, negative scale | Yes | Yes | Yes |
| Alpha and supported blend mapping | Yes | Yes | Yes |
| Exact light RGB tint | Yes | Yes | No |
| Two-color tint | Yes | Yes | No |
| Gamma/Linear working space | Both, with target limits below | Both, with target limits below | Gamma downgrade only |
| Custom Cane batching shader | Yes | Yes | No |

Canvas2D uses Pixi's official Canvas renderer extension and draws Core-authored
triangles. It is not advertised as shader-equivalent: it preserves geometry,
UV, alpha, clipping, affine projection, and Canvas-supported blend modes, but
does not silently approximate Cane light/dark or linear-light shader math.

Gamma is the default. Linear requires a single encoded base-level color target
without depth/stencil or host masks; WebGL2 also requires floating-point color
attachments. It includes the actual prior destination in a floating-point pass,
then encodes back for native Pixi content. See the [Pixi integration guide](../cane-ts/cane-pixi-v8/README.md#texture-and-color-contract)
for target limits and performance costs, and [render profiles](RENDER_COLOR_SPACES.md)
for the shared color rules. Core clipping remains valid in both modes.

## LayaAir 3.4.1 renderer capability matrix

| Capability | WebGL | WebGPU | native / NoRender |
| --- | :---: | :---: | :---: |
| Region, Mesh, Weighted Mesh, Deform, Linked Mesh, Sequence | Yes | Yes | Rejected |
| Core clipping and clipping-aware Slot objects | Yes | Yes | Rejected |
| Full affine, shear, reflection and negative scale | Yes | Yes | Rejected |
| Light/dark tint, alpha, straight/PMA and sRGB/linear metadata | Yes | Yes | Rejected |
| Normal, additive, multiply and screen | Yes | Yes | Rejected |
| Retained `Mesh2D` uploads and explicit batch-split statistics | Yes | Yes | Rejected |
| Automatic/manual/multi-instance scheduling | Yes | Yes | Not renderer-dependent |

Both exact 3.4.1 web drivers are exercised with official release artifacts. The adapter uses the
same `Shader3D`, `Mesh2D`, material and final-frame projection for both, leaving GLSL translation
and GPU resource creation to LayaAir's driver factories. There is no advertised Canvas2D path in this package. LayaAir 3.4.1 also does not recreate every
engine-owned WebGL resource or a lost WebGPU device in place; Cane suspends projection and reports
`contextRestoreFailed` until the host recreates the engine/runtime realm. This limitation does not
cause another Core sample or solve.

## Cocos Creator 3.8.8 renderer capability matrix

| Capability | WebGL | WebGPU | Windows Native |
| --- | :---: | :---: | :---: |
| Region, Mesh, Weighted Mesh, Deform, Linked Mesh, Sequence | Yes | Yes | Yes |
| Core clipping and draw-order Slot objects | Yes | Yes | Yes |
| Full affine, shear, reflection and negative scale | Yes | Yes | Yes |
| Light/dark tint, alpha, straight/PMA and sRGB/linear metadata | Yes | Yes | Yes |
| Normal, additive, multiply and screen | Yes | Yes | Yes |
| Gamma/Linear working space | WebGL2: Gamma Direct; both in IsolatedSurface | Gamma Direct; both in IsolatedSurface | GLES3: Gamma Direct; both in IsolatedSurface |
| Unified automatic/manual/multi-instance scheduling | Yes | Yes | Yes |
| Retained uploads and explicit split/reallocation statistics | Yes | Yes | Yes |

All three routes consume the same Core `RuntimeRenderPacketV1`. WebGL and WebGPU use the Cocos 2D
assembler/RenderEntity flow; Native uses exact-3.8.8 dynamic middleware/SUB_NODE draw infos. WebGL texture restoration is supported. Creator 3.8.8 does not provide complete
in-place WebGPU device recreation, so device loss suspends the component and reports `contextLost`
until the host recreates the renderer/runtime realm.

The selectable color profile has separate validation scope from the existing
backend integrations. WebGL2, WebGPU and Windows Native GLES3 each pass 80 GPU
color checks covering Gamma/Linear, source color/alpha metadata, tint, four blend
operations and filtering. Portal rendering and paused mode switching pass on all
three; WebGPU and Windows Native also pass disable/re-enable checks. Android,
iOS and other native graphics backends remain unverified. Linear requires
an explicit per-character `IsolatedSurface`: other Canvas nodes and the scene
backdrop do not participate in its internal composition, and native Slot objects
cannot be interleaved inside it. Keep `Direct + Gamma` for native scene ordering.
See the [Cocos API guide](../cane-ts/cane-cocos-3.8.8/docs/API.md) for float texture,
surface-size and effect reimport requirements.

## SemVer policy

- Package `0.y.z` is pre-release API: a minor increment may contain breaking
  package/API changes, while a patch increment remains backward-compatible.
- Starting at package `1.0.0`, removals or incompatible type/behavior changes
  require a package major; additive API is a minor; fixes are patches.
- Runtime API 1.x evolves additively by minor plus capability bits. A host must
  discover the advertised range and required bits instead of inferring it from
  the npm package version.
- Runtime Format/CANEB/Atlas versions do not change when API-only facilities
  such as dynamic resources, Bounds, host motion, or geometry modifiers are
  added.
- Conformance status applies only to the exact suite version and manifest
  digest reported by `runtimeCapabilitiesV1()`.

TypeScript packages are marked `private: true` and `UNLICENSED`; the public
repository does not imply publication to an npm, NuGet or engine registry.
Source builds and local engine-package installation are described in
[building from source](BUILDING.md).
