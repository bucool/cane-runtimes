# Rendering contract and current limits

The adapter consumes `RuntimeFrame.render_packet()` in published attachment order.
Indexed triangles are expanded for upload without changing coordinates or UVs.
It does not apply `source_affine`, inspect source slots to infer draw order, solve
constraints, resample animation, or run skinning/clipping again.

## Shared packet shader

`BlendColorSpace` selects `Gamma` (default) or `Linear` on each scene component,
UMG widget or isolated surface. The option changes filtering, final tint application
and all four blends; it does not change Core data or its canonical texture contract.
Source sRGB is decoded first. Canonical PMA textures are unpremultiplied in linear
space, converted to the selected space and premultiplied there **before filtering**.
Gamma uses sRGB-valued final tint; Linear decodes that tint. Alpha is never gamma
corrected. The [shared color-space guide](../../../docs/RENDER_COLOR_SPACES.md)
describes the presentation profiles.

Both presentation paths use the same global shader:

- raw texture texels are decoded from sRGB when declared;
- conversion to the selected working space precedes interpolation, with independent min/mag and per-axis wrapping;
- straight/PMA source modes and final light/dark tint are explicit;
- normal, add, multiply and screen use their own hardware blend states;
- rasterization is two-sided, preserving reflected and clipped final triangles;
- queued work retains final vertices and RHI references without reading UObjects
  from a render pass; missing or mismatched GPU textures report a host error.

For premultiplied source RGB `Cs`, source alpha `As`, destination `Cd/Ad` in the
selected working space, the blend pipeline uses:

| Mode | RGB | Alpha |
| --- | --- | --- |
| normal | `Cs + Cd*(1-As)` | `As + Ad*(1-As)` |
| add | `Cs + Cd` | `As + Ad` |
| multiply | `Cs*Cd + Cd*(1-As)` | `As + Ad*(1-As)` |
| screen | `Cs + Cd - Cs*Cd` | `As + Ad*(1-As)` |

## Direct scene mode

`UCaneComponent.Rendering = DirectScene` is the default. A scene-view extension
inserts final triangles at `BeforeDOF`. Core XY maps once to Unreal X/Z, followed
by component and camera transforms. Large-world pre-view translation is applied
before narrowing the clip matrix to shader floats.

The pass removes pre-exposure from scene color into a linear RGBA16F target,
converts the actual scene background into each instance's selected working space,
blends its packet, then returns to linear and restores pre-exposure. Consecutive
instances with the same choice share that working space; transitions preserve
draw order. Opaque scene RGB is converted directly because Unreal scene alpha
does not always represent transparency coverage. Alpha is
not exposure-scaled or clamped to one. This path is intended to let multiply and
screen affect scene content, without flattening a character first.

Resolved reversed-Z depth rejects fragments behind opaque geometry. Cane does
not write scene depth. Instances sort by translucency priority, then the view's
distance/projected-Z/axis policy, with stable ties; attachment order within each
packet remains authoritative. Each world has its own owned render snapshot.
Hidden/show-only primitive sets and game/editor visibility are respected.

This is a deferred SM5+ path with post-processing enabled. Instanced stereo,
mobile multiview, multisampled depth and mismatched depth/color texture grids are
excluded. Native D3D12 pixel evidence covers orthographic scene capture, all four
blends over opaque background geometry, real depth occlusion, hidden/show-only
lists, host transforms and pre-exposure at both 1 and 2. Viewport offsets, dynamic
resolution and combined world/view configurations require broader acceptance.

Direct scene rendering is a separate composition pass, not an ordinary Unreal
mesh draw. It cannot interleave Cane attachments with arbitrary translucent mesh
draws. It provides no lights, shadows, hit proxies, custom depth, velocity or
per-bone 3D intersections. Temporal effects and depth of field can therefore use
background depth/velocity; those effects require explicit application evaluation.
Select actors through World Outliner. Do not assume support in wireframe, path
tracing, reflection captures or passes which omit normal post-processing.

## Direct UMG

`UCaneWidget.Rendering = DirectSlate` uses UE 5.8's RDG custom Slate element API.
Each paint retains an owned final publication and GPU texture references. Core is
not sampled from `OnPaint` or the render thread. The draw occurs in Slate layer
order and blends against the target's already-painted contents.

The widget rectangle maps Cane's Y-up geometry into Slate's Y-down layout once.
Inherited render transforms, reflection, color/opacity and the active Slate clip
quads are applied as presentation state. A bounded background copy is converted
to the selected premultiplied RGBA16F working space for packet blending, then returned to the target's
encoding. Final writes respect both the widget rectangle and ancestor clipping.

`TargetColorSpace = Auto` uses ordinary gamma-encoded 8-bit Slate/retainer targets
and linear float targets. `Linear` and `SlateSRGB` override that choice for custom
offscreen renderers: UE's custom draw inputs do not expose the caller's gamma flag.
This path is single-sampled. Native HDR display planes are explicitly rejected;
the regular SDR UI plane used by an HDR desktop is a separate composition domain.
Retainers can only blend with contents inside their own target, not widgets behind
the retainer or a scene subsequently combined by Unreal's HDR compositor.

Native tests verify all four blends over earlier widgets, inherited opacity,
rotation/reflection, parent clipping, clearing/release, gamma-encoded targets and
actual Retainer caching/invalidation. Hardware sRGB on a Retainer target is
handled separately from Slate's UI gamma encoding. Paint objects survive weak
Slate references and are retired through deferred render cleanup.

## Isolated surface

`IsolatedSurface` draws into a transparent RGBA16F texture in the selected space.
Gamma results are converted back to linear PMA before the public surface texture
is presented as one two-sided unlit AlphaComposite quad. No material/shader change
is needed when switching the mode. A widget can select the same mode
to use an AlphaComposite UI material and ordinary Slate image layout/clipping.

The asset's surface rectangle and resolution stay fixed during playback. Content
outside the rectangle is clipped; magnification can expose surface pixels.

Color-space transfer preserves partial-alpha PMA by unpremultiplying, converting
straight RGB, then multiplying by coverage. Coverage is `min(alpha, 1)`; additive
alpha above one is preserved separately and never divides opaque radiance.
Nonzero RGB with zero alpha is not a supported transparent-surface encoding.

## Limitations requiring application decisions

- In isolated mode, multiply/screen see earlier content **inside the character
  surface**, not external scene objects or other UMG widgets. DirectScene and
  DirectSlate blend within their corresponding scene/UI target instead.
- Final presentation follows Unreal exposure and tonemapping. Isolated scene
  quads also follow Unreal translucency sorting.
- Additive alpha can exceed one inside RGBA16F. The isolated surface's final Unreal
  material opacity may clamp it; cross-surface additive-alpha is not verified.
- This is a Win64 SM5+ source target. Mobile, VR, macOS/Linux, forward/mobile
  rendering and other Unreal versions have no engine acceptance evidence.
- Imported texture replacement must retain the declared dimensions and raw-byte
  sampling settings. Runtime introduction of new texture objects is not exposed.
- Bone anchors follow full-affine positions. General sheared object transforms
  require using the returned basis vectors in the application's geometry path.

UE 5.8.3 UHT/C++/global shader builds and nine native checks passed in two fresh
D3D12 processes. Coverage includes the surface, scene and UMG cases listed above,
Spineboy's 11-animation JSON/CANEB parity, save/reimport, player events, mirrored
bone offsets, pause/resume and asset clearing/reload. Win64 cooking and a packaged
game also passed: the executable loaded the saved Spineboy asset, advanced `run`
and read back its GPU surface with the import source files unavailable. These
results do not establish support outside the listed rendering paths and platform
boundaries.
