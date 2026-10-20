# Runtime architecture

## Native C# and Unity v2

The native [C# family](../cane-csharp/README.md) implements the
[v2 contract](../specification/runtime/RUNTIME_V2.md) and loads existing v2 JSON
and `.cane2` exports without changing the animation or texture resources.
Core targets .NET Standard 2.1, owns animation, constraints and final geometry,
and has no Unity, Rust, native interop or third-party dependency. Shared indexed
data is immutable to callers; each instance owns its state and reusable output.
Frames are borrowed until the next evaluation.

Unity owns resources, mesh/Canvas rendering and game integration. Optional URP,
UI Toolkit and Addressables packages share the same Core and player; they do not
resample animation or reconstruct final geometry. The historical v1 evaluator,
API facade and export formats are not retained in this implementation.

## Language boundaries

Each language owns one independent Core and its engine adapters:

- `cane-ts/cane-core`: TypeScript Core; PixiJS, LayaAir and Cocos adapters are sibling packages.
- `cane-csharp/cane-core`: native v2 managed Core; Unity integration is in `cane-unity`.
- `cane-cpp/cane-core`: independent native Core; Godot integration is in `cane-godot`
  and the UE 5.8 source integration is in `cane-unreal`.

Language Cores share the [specification](../specification/README.md), not an FFI
implementation or a dependency on the editor/Rust runtime.

## Core ownership

Core owns decoding and validation, animation sampling, tracks and mixing,
affine transforms, constraints and Physics, skins and deformation, clipping,
resource overlays, queries, final tint and final render geometry. Player state
and published-frame ownership follow the Runtime API contract. Required unknown
features fail explicitly.

## Engine ownership

Adapters load and release engine resources, map coordinates once, upload final
vertices/UVs/indices, apply the declared color and blend contract, preserve draw
order, and integrate engine lifecycle and scheduling. They must not resample
animation, solve constraints or reconstruct authoritative geometry or colors.
Core must not depend on engine objects, editor tools or authoring state.

## Rendering and compatibility

TypeScript adapters consume the same Core packet. Independent language Cores
target the same specified output. GPU sampling and precision remain backend
properties; [compatibility limits](COMPATIBILITY.md) are documented separately.
Format, API, package and engine versions are separate compatibility domains.
The specification lock identifies a target and does not certify a build.

Renderer-owned [Gamma and Linear profiles](RENDER_COLOR_SPACES.md) select texture,
tint and destination blending space without altering Core packets or sampling.

Runtime source builds do not require development tests, benchmarks or examples.
Build prerequisites and commands are in [BUILDING.md](BUILDING.md).
