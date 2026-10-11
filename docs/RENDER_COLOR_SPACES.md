# Rendering color spaces

The renderer's **Blend Color Space** selects the RGB working space for texture
filtering, material tint and layer blending. It does not change animation
sampling, geometry, exported texture bytes or RenderPacket metadata.

- **Gamma** (default) uses sRGB-encoded RGB for 2D artwork composition.
- **Linear** uses linear RGB for composition and lighting integration. This is
  the baseline color algebra in the existing Geometry and Render Algorithms v1.

Alpha is a linear coverage value in both profiles. The Normal, Add, Multiply and
Screen equations are unchanged; their RGB operands use the selected working
space. The editor preview and the destination runtime should use the same profile.
Neither profile alone guarantees matching pixels if filtering, backdrop, frame
time or imported image pixels differ.

## Texture and tint normalization

The texture's `colorSpace` describes storage, not the requested blending mode.
Decode stored sRGB to linear first. A canonical `premultiplied` texture contains
linear RGB multiplied by alpha after that decoding, as specified by the format.
For Gamma, recover straight linear RGB, encode that straight RGB to sRGB and
premultiply in Gamma. Do this **per texel before interpolation**. Straight-alpha
textures are transferred into the selected space without changing their alpha
or associating RGB with it. Preserve the source's straight/PMA representation
through interpolation; recover straight color from a filtered PMA sample before
applying tint.

RenderPacket light/dark tint is straight sRGB. Gamma uses those normalized RGB
values directly; Linear decodes them. Apply tint in the selected space and
premultiply the final fragment once. Never relabel the atlas or skip its required
PMA decoding to obtain a different blending appearance.

## Destination and presentation

Source and destination RGB must be in the same selected space when blending.
Encoding only a source shader's output does not provide Gamma composition on a
linear render target. Adapters either use a matching native target or perform an
explicit color-space boundary around a render pass.

At a boundary carrying transparent PMA pixels, divide RGB by coverage, apply the
transfer and multiply by coverage again. Coverage for the transfer is alpha
clamped to [0,1]; this avoids dividing opaque radiance by accumulated additive
alpha greater than one. The original alpha is preserved until target-format
clamping. Opaque destinations transfer RGB directly. Display encoding occurs
after composition, separately from optional monitor color management.

Host sprites, post-processing and lighting outside a Cane pass still follow the
host engine's rules. Engine integration guides describe the supported targets
and how to select the profile; a Core conformance result does not certify these
GPU paths.

## Choosing a profile

| Adapter | Switch | Scope and supported targets |
| --- | --- | --- |
| [PixiJS](../cane-ts/cane-pixi-v8/README.md#texture-and-color-contract) | `character.blendColorSpace = "linear"` | Ordered Cane segments include the previous Pixi destination. Linear needs WebGL2 float targets or WebGPU; no depth/stencil target or host mask. |
| [LayaAir](../cane-ts/cane-layaair-3.4.1/README.md#gamma-and-linear-composition) | `setCaneLayaColorBlendingV1("linear")` | Complete 2D stage; all actors share the profile. Mixed 3D camera stages are rejected. |
| [Cocos Creator](../cane-ts/cane-cocos-3.8.8/docs/API.md) | `hero.setColorProfile(CaneCocosBlendColorSpaceV1.Linear, CaneCocosCompositionModeV1.IsolatedSurface)` | Direct Canvas drawing defaults to Gamma. Linear requires an explicit per-character isolated surface; other Canvas nodes and scene backdrop are outside that composition. Native Slot interleaving is rejected in isolated mode. Verified on WebGL2, WebGPU and Windows Native GLES3; other native platforms are unverified. |
| [Unity](../cane-csharp/cane-unity/docs/COLOR_BLENDING.md) | `CaneCameraRenderer.BlendColorSpace` | Camera pass, including earlier destination and native Slot content. Keep the Unity project in Linear; Gamma supports a single 2D view. URP/HDRP expose the same setting. |
| [Godot](../cane-cpp/cane-godot/docs/COLOR_BLENDING.md) | `CaneSkeleton.blend_color_space` | `0` Gamma, `1` Linear. Keep HDR 2D enabled. Transparent Gamma requires Compatibility; Forward+/Mobile transparent targets can use Linear. |
| [Unreal](../cane-cpp/cane-unreal/docs/API.md) | `BlendColorSpace` / `SetBlendColorSpace` | Component, UMG widget and isolated surface. Public isolated textures remain linear PMA for engine materials. |

The standalone language cores do not select a GPU profile. Hosts using owned
RenderPackets implement the chosen renderer profile at the graphics boundary.
