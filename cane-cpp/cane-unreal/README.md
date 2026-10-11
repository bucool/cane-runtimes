# Cane Unreal Engine adapter

**English** | [简体中文](README.zh-CN.md)

Source integration targeting **Unreal Engine 5.8 / Windows x64**. This adapter uses
the independent [C++ Core](../README.md), targeting Runtime JSON/CANEB 1.1,
Atlas 1.0 and Runtime API 1.13. It does not use Rust, a C ABI, or the Cane editor.

**Verified target:** UHT, Editor/Game compilation and nine native automation
checks passed with UE 5.8.3 / Win64 D3D12 in two fresh engine processes. Coverage
includes import/reimport, playback events, lifecycle, four surface blend modes,
scene background/depth/exposure, bone anchors, and direct/isolated UMG pixels with
transforms, clipping and Retainer caching. A cooked Win64 game also loaded and
rendered Spineboy with its import source files unavailable. This is a source
integration with the limits described below.

The source contains a runtime module and an editor import module:

- `UCaneSkeletonAsset`: imported native data, Atlas descriptions, decoded textures
  and generated display materials, retained for cooking.
- `UCanePlayer`: independent playback, tracks, mixing, events and host controls.
- `ACaneActor` / `UCaneComponent`: scene presentation and editor preview.
- `UCaneWidget`: UMG presentation with its own player.
- `UCaneBoneAnchorComponent`: an exact affine position anchor for attaching objects.
- `UCaneSurface`: a fixed-resolution linear premultiplied composition surface.

Scene components, UMG widgets and isolated surfaces expose `BlendColorSpace`:
`Gamma` is the default and `Linear` is selectable at runtime. The selected space
applies to filtering, tinting and all four blend modes. The public isolated texture
stays linear PMA for Unreal materials. See the [API guide](docs/API.md).

Core owns animation, constraints, deformation, clipping, final geometry, UVs,
tint and attachment order. The adapter uploads the final packet and owns Unreal
assets, GPU resources and lifecycle. Runtime and editor modules are separate;
the game does not need the source export directory after cooking.

Read [installation](docs/INSTALLATION.md), [usage and API](docs/API.md) and
[rendering and limitations](docs/RENDERING.md) before integrating. The default
scene path draws final triangles into scene color before depth of field, with
depth rejection and pre-exposure compensation. An optional isolated surface is
also available. UMG defaults to a direct Slate packet path with verified background
blending, clipping and retainer checks. The optional surface path cannot multiply
or screen against external scene objects or other widgets. Native HDR UI planes,
stereo and multisampled scene depth are outside this target; advanced combinations
of worlds and viewports remain unverified.
