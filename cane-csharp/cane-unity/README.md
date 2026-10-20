# Cane for Unity

Native C# Core and a Unity 6.3 adapter for existing Cane v2 JSON and `.cane2`
exports. Validated with Unity 6000.3.23f1 on Windows x64 Mono and D3D11,
including Gamma and Linear rendering.

Add `cane-csharp/cane-core/package.json`, then this package's `package.json` via
Unity Package Manager **Add package from disk**. Canvas support is compiled when
`com.unity.ugui` is installed. Core has no Unity dependency.

## Scene characters

Copy a `.cane2` and its unchanged relative texture files into `Assets`. The
importer creates a `CaneAsset`; its inspector can create a scene character.
The inspector preview selects setup pose, animation and skin, with playback and
a time slider. It renders through the same player and mesh adapter as the game.
For JSON, select the JSON TextAsset and use **Assets → Cane → Create from JSON**.
That command creates an asset containing the original document bytes and imported
textures; run it again when the source JSON changes.

`CanePlayer` owns animation state, pose and lifecycle. `CaneRenderer` sends Core's
final geometry to MeshRenderer. The default scale is 0.01 Unity units per Cane
pixel. GameObject transforms move the character independently of its pose.

```csharp
using Cane.Unity;
using UnityEngine;

public sealed class Character : MonoBehaviour
{
    public CanePlayer Player;
    void Start()
    {
        Player.Play("walk", loop: true, mix: .15);
        Player.AnimationEvent += e => {
            if (e.Type == Cane.EventType.Event) Debug.Log(e.Key.Value.Name);
        };
    }
}
```

Use `State.Set`, `State.Add`, `State.Clear` and `TrackEntry` for multiple tracks,
queues, interruptions, additive layers, thresholds, seeking, reverse and
custom mixing curves. `BeforeWorld` is the place to write pose or constraint
channels after animation sampling. `AfterWorld` exposes solved bone transforms;
`AfterGeometry` exposes final render geometry. Event callbacks are dispatched
after evaluation and may select new animations safely.

`UpdateMode` selects normal, fixed, unscaled, late or manual updates. Late updates
run after normal game updates and before the adapter uploads geometry. In manual mode,
call `Advance(delta)` and `CaneRenderer.Upload()`. Disabling a player keeps its
state; destroying it releases its asset resource reference. `Initialize` rebuilds
the instance. `CaneAsset` shares decoded data and GPU resources across characters.
Reimporting an asset invalidates its cache; players rebuild on their next advance.
For programmatic document/texture changes, call `CaneAsset.Reload()`. Previous
resource generations remain valid until their last user releases them. Direct
`Acquire()` calls must be paired with `Release(theAcquiredResources)`.

For application-managed loading, decode bytes with `RuntimeAsset.Load`, provide
textures in export order to `CaneResources`, then pass those resources to
`CanePlayer.Initialize`. That caller owns the resources and must dispose them
after its characters. Texture ownership transfers only when explicitly requested.

## Appearance and game integration

Core owns skins, constraints, deformation, sequences, clipping, draw order and
queries. `Skeleton.SetSkins`, `Skeleton.AddAttachment`, `Frame.SetAttachment` and
`Frame.Deform` support runtime appearance changes. `CaneResources.AddTexture` and
`ReplaceTexture` provide GPU overlays without modifying the shared Core asset.
`SpriteAttachment.Create` converts Unity Sprite pivots, tight geometry and atlas
UVs into a Core attachment. Register its texture once and reuse that index.

`CaneAtlas` provides runtime image repacking for the selected skin and attachment
overrides. Create one reusable owner per skeleton:

```csharp
var atlas = new CaneAtlas(player.Skeleton, player.Resources);
atlas.Pack(maxSize: 2048, padding: 2);
renderer.Upload();
// Call Pack again after changing skins or attachments.
// Dispose before releasing the player/resources:
atlas.Dispose();
```

Packing reads the current working textures on the GPU; readable CPU copies are
not required. It copies full-resolution texels, including sequence frames, and
changes instance UVs once. The original exports, shared attachments, geometry,
deformation and timeline bindings stay intact. Repacking reuses released texture
slots. Disposal restores the source attachments and releases generated pages;
an independently replaced attachment is left alone. Edit the source appearance
between packs, since edits to a mapped copy are discarded when it is restored.
Paused scene/Canvas/Toolkit displays need their usual upload or refresh after
packing and disposal.

To retain a scene material and its auxiliary maps, pass a material template and
the shader texture properties to pack. Each array is indexed by the original
texture index:

```csharp
atlas.Pack(material: surface, layers: new Dictionary<string, Texture[]> {
    ["_BumpMap"] = normalMaps,
    ["_MetallicGlossMap"] = glossMaps,
    ["_EmissionMap"] = emissionMaps
});
```

Maps keep their own resolution, precision and sampling. A shared integer texel
grid aligns different layer sizes without resampling. Missing entries use the
template's texture, or the shader's default texture. List every texture property
that samples the attachment UVs; unlisted properties retain the template value.
Texture scale/offset and Cane outline bounds are remapped. These materials keep
each source texture on a separate page to retain their sampling range.
Mipmapped, oversized or incompatible-resolution regions retain their originals.
`maxSize` limits every output layer, not just its base texture.

`CaneResources.SetMaterialTemplate(texture, material)` snapshots a scene shader,
surface properties and keywords for an original texture too. Texture, alpha,
sampling and draw blend settings remain resource-managed; Canvas keeps its UI
shader. Template textures remain caller-owned. Packaged scene shaders still need
a serialized material reference. Explicit `SlotMaterials` take precedence over
resource materials; clear those overrides to use the generated atlas materials,
or assign the generated resource materials yourself.

Padding copies neighboring source texels
with the original wrap mode. Atlas coordinates can produce small hardware-filtering
differences, especially with forced anisotropic filtering; byte-identical texels
do not imply identical filtered screen pixels.

`CaneBoneFollower` follows solved bones. Unity Transforms cannot represent shear;
the follower aligns its rendered X axis and preserves axis lengths and handedness,
including under rotated, nonuniformly scaled parents. Point followers use the same
direction conversion. Full affine matrices remain
available through `Skeleton.World`. Core also exposes bounding, point, path,
hit/segment and clipping-boundary queries for game logic.

`CaneBoneDriver` supplies a Transform as a bone input. Set its `Player`, bone
ID/name and optional `Target` (default: the component's own Transform), then call
`Rebind()`. World-space mode accepts independent objects or physics bodies; local
mode uses the target's local TRS and is useful with a mirrored bone hierarchy.
Choose position/rotation/scale/shear and `Mix`. Positions convert by the player's
units per pixel; Core owns inheritance and affine decomposition. Inputs resolve
once before constraints against current animated parents, so IK, local Transform,
Slider and Physics constraints consume them in their normal order. Constraints
can modify the input afterward. Disabling the driver restores the current
animation/setup pose; reinitializing the player rebinds it. The most recently bound
driver owns a bone; releasing an older driver cannot remove a newer input.

A Rigidbody2D/Rigidbody or hinge chain can drive the target Transform directly.
Use normal/late updates after Unity physics, or advance manually after a custom
simulation step. Bone driving and following do not require physics modules when
used with ordinary Transforms. They neither resample animations nor solve the
constraints a second time.
`CanePointFollower` follows the selected point attachment and reports `HasPoint`.
`CaneCollider2D` follows a bounding attachment with a native `PolygonCollider2D`
when Unity's Physics 2D module is installed. Hidden or replaced bounding attachments
disable that collider. These components reuse evaluated geometry; they do not solve
the animation again. Call `Rebind()` after changing a follower's player/bone/slot.

`CaneRenderer.SlotMaterials` allows per-slot material overrides. Custom materials
must interpret the final vertex data and texture alpha contract. The default
shader supports one/two-color tint and normal, additive, multiply and screen
blending, with culling off and depth writes off.
`CaneRenderer.MaskInteraction` selects visibility inside/outside native Unity
SpriteMasks; mask sorting ranges and SortingGroups use Unity's scene ordering.
Custom slot materials must provide their own stencil settings.

## Material effects

The scene/Canvas `Cane/Skeleton` shader and scene `Cane/Lit` shader provide
hue, saturation, brightness, secondary albedo, grayscale, fill and outlines.
The optional URP shaders use the same surface controls. Clone a resource
material before changing it for one character, then
assign it through `SlotMaterials`; resource materials are shared.

Enable **Material effects** in the material inspector, or set its property:

```csharp
material.SetFloat("_Effects", 1);
material.SetFloat("_Hue", 30); // degrees
material.SetFloat("_Saturation", .8f); // 1 = original saturation
material.SetFloat("_Brightness", 1.1f); // 1 = original brightness
material.SetFloat("_Grayscale", 1); // 0 = original, 1 = grayscale
material.SetColor("_FillColor", new Color(1, 0, 0, .5f)); // alpha = fill strength
material.SetColor("_OutlineColor", Color.black);
material.SetFloat("_OutlineWidth", 1);
```

The fill changes surface color without changing coverage. Outline color alpha
controls the border's opacity; attachment and Canvas opacity also fade the border.
Width is in source texels by default. Set `_OutlineScreenSpace` to 1 for screen
pixels and `_OutlineOnly` to 1 to display just the border. The effect runs before
scene lighting and fog; grayscale surfaces can still receive colored light.
Set `_Effects` to 0 to bypass these controls. One uniform material switch controls
the effect; no separate shader keyword is required. Default values retain the
ordinary appearance with the switch on too.

HSV adjustment operates in perceptual RGB in either project color space.
`_SecondaryTex` supplies an additional albedo image, with ordinary texture
scale/offset and `_SecondaryColor` tint. Its alpha times the tint alpha controls
the blend strength; the original texture coverage is retained. It runs before
HSV, grayscale and fill. Include `_SecondaryTex` in `CaneAtlas`'s named layers
when repacking it with the attachments.

`Cane/Lit` and `Cane/URP/Lit` additionally provide `_LightRamp` and
`_RampStrength`. The ramp's horizontal coordinate is the clamped dot product of
the mapped normal and light direction. It replaces direct diffuse response,
retaining light color, attenuation and shadows; metallic specular, ambient
lighting and emission remain native. Use per-pixel lights for a per-light ramp
in the Built-in pipeline. The ramp is independent of attachment UVs and must
not be repacked as an attachment image.

`_RimColor` (alpha = strength) and `_RimPower` add view-dependent edge emission
to those two lit shaders. The result follows mapped normals, fades with the
attachment and is applied once even with multiple lights. Ramp and rim default
to zero strength, preserving the accepted native lighting.

Outlines sample neighboring texture alpha inside the existing mesh. Transparent
padding is needed for an outer border; tight meshes and Core clipping constrain
its extent. `_OutlineUvRect` is the region's normalized `(minU, minV, maxU, maxV)`
rectangle in Unity texture coordinates, after the adapter's V flip. Set it on
a slot material to exclude neighboring atlas regions. Existing exports are not
modified or expanded. Effects do not change shadow/depth coverage.

Canvas materials retain the default shader's stencil, clipping and tint settings.
When changing properties under a native `Mask`, update the generated graphic's
`materialForRendering` too: Unity maintains a separate stencil material, as it
does for other UGUI material effects.

## Animator and Timeline

Add `CaneAnimator` beside `CanePlayer` and Unity's `Animator`. Its inspector can
create a controller with control clips for the existing Cane animations. Control
clips carry Unity timing; Core still evaluates the original animation data. Clip
names match export IDs or animation names unless overridden in `Bindings`.

After editing controller timing, layers or blend trees, use **Prepare controller
bindings**. It records cycle offsets, time parameters and additive layers. Repeated
clips within a blend tree receive distinct Unity control-clip objects because
Unity omits zero-weight inputs from its runtime query. This edits the Unity
controller, not the Cane files. State paths in bindings include their layer and
sub-state-machine names. Additive layers use Cane's setup-relative semantics.

Animator layers occupy tracks starting at `FirstTrack`. The bridge copies native
weights/times; a shared clock evaluates Core once afterward. Use the Animator's
speed to control its timeline. `Synchronize(delta, emitEvents: false)` suppresses
crossed events for a manual seek; manual callers then invoke `player.Advance(0)`.
Call `Rebind()` after changing bindings. Disabling the bridge releases its tracks
and restores the player's previous update mode.

Installing Unity Timeline 1.8 or newer enables the optional `Cane.Unity.Timeline`
assembly. Add a `CaneAnimationTrack`, bind it to a player, and select the animation
ID/name on each `CaneAnimationClip`. Clips use native Timeline weights, clip-in,
speed, extrapolation and looping; `Reverse` reverses the animation. Tracks can
be additive. Explicit evaluation/scrubbing suppresses crossed keyed events.
Stopping the director releases its tracks and clock. Reserve separate track
indices for multiple Timeline tracks and for Animator layers on the same player.
Physics uses the host clock; use the same scaled/unscaled timing for graphs that
share a player.

`CaneRootMotion` extracts translation from a bone (ID/name; empty selects the first
bone). Choose X/Y, local movement `Scale`, and animation `Tracks` (default track 0;
empty selects all). Call `Rebind()` after replacing the bone or track selection.
Unselected axes and tracks remain in the skeleton. Extraction runs before
`BeforeWorld`, so game code can still edit the pose. The host moves after Core has
solved the current parent basis and before `AfterWorld` followers run.

By default it moves the player's Transform. Assign `Body2D` or `Body` to accumulate
movement until `FixedUpdate`, then apply it once with `MovePosition`. Prefer the
player's `FixedUpdate` mode for physics. Manual physics simulations call
`ApplyPendingMotion()` before their simulation step. Physics modules are optional;
their body fields are compiled only when those Unity modules are installed.
For a character controller or custom collision handling, disable `AutoApply` and
consume world-space `DeltaPosition` from `Moved`. The host's actual movement enters
Cane Physics at the next evaluation. Disabling/rebuilding releases the old
extractor and discards pending movement. Native Animator and Timeline use the same
extractor; explicit seeks/scrubbing do not move the host.

`root.TryFitToDistance(entry, toTime, targetPosition - player.transform.position)`
adjusts `Scale` for the entry's remaining displacement. `toTime` is an elapsed
entry time, including completed loops. Use it with a fully weighted entry; it
samples the original curves and converts through the current parent/host basis.
It does not predict future animation blends or parent/constraint motion. Refit
as the target or basis changes. An unreachable distance (disabled/unkeyed axis,
finished motion, singular transform or an out-of-plane target) returns false and
keeps the existing scale. Queries do not seek, emit events or allocate each frame.

`CaneRenderSeparator.SetSlots` splits the final mesh after the specified slot IDs
or names. Its child renderers follow animated draw order and retain Core clipping.
The attached `SortingGroup` controls the whole character's scene order. Other
renderers under that group can use `OrderAfterSlot(slotIndex)` from an `Uploaded`
callback to remain between the correct parts. Disabling the separator restores
the single renderer. External objects still use their own material/masking rules.

For an object that should inherit a slot's appearance, add `CaneSlotObject` to the
scene character. Set `Slot` (ID/name), `Content` and optionally `Before`, then call
`Rebind()`. It installs a separator and follows the evaluated bone, including
shear/reflection, while retaining the content's local position, rotation and scale.
Local offsets use Unity units. The object follows animated draw order; by default,
removing the slot attachment hides it. Set `FollowAttachment = false` to keep it
visible while the slot is active. The component runs in Play mode.

Native `SpriteRenderer` objects with `Sprites/Default` and meshes using
`Cane/Skeleton` or `Cane/Lit` inherit slot/skeleton opacity and Core clipping. Sprite tint and
native SpriteMasks continue to apply. Concave, convex and inverse clips use Core's
evaluated triangle partition, including deformed contours. This optional GPU mask
requires Shader Model 4.5; ordinary skeleton rendering still targets 3.5.
Custom materials retain their shader. To inherit opacity/clipping, expose
`_CaneSlotAlpha` (Float, default 1) and `_CaneClipCount` (Integer, default -1), include
`Runtime/Resources/Cane/SlotObject.hlsl`, and pass world position and premultiplied
RGBA to `CaneApplySlot` in the fragment shader. Other shaders still follow the
object's transform/order, but do not inherit that appearance automatically.

Materials are cloned at binding and restored, with the original property blocks
and parent, on release. The component does not destroy the content. Rebinding the
same content transfers ownership; an older binding cannot detach its replacement.
Call `Rebind()` after changing content, slot, child renderers, materials or property
blocks. Changes to those materials/blocks while bound are managed by the binding.
Use `CaneSlotGraphic` below for Canvas content; `CaneSlotObject` binds scene Renderers.

## Canvas

Use `CaneGraphic` with a `CanePlayer` instead of `CaneRenderer`. Set the player's
units per pixel to the scale used by the Canvas (often 1). Consecutive material
groups use maskable children; oversized draws split to respect CanvasRenderer's
16-bit index limit without dropping triangles. Core clipping, RectMask2D, Mask
and geometry-based raycast checks are supported by the adapter. Graphic tint and
CanvasGroup opacity apply after Core's color composition and clamp. Canvas uses
separate tint data so Unity's vertex-color conversion cannot alter the animation
colors. Screen-space overlay uses an always-pass depth comparison; camera/world
Canvas keeps depth testing.

`CaneGraphic.SlotMaterials` overrides materials by slot index. Start from a Canvas
material returned by `CaneResources.Material(..., canvas: true)`; custom shaders
must preserve Canvas tint, stencil and clipping conventions as well as Core's
final vertex contract. Set `overlay: true` for an overlay material's depth state.

Add `CaneSlotGraphic` beside `CaneGraphic` to insert a UI `Content` RectTransform
before or after a named `Slot`. Call `Rebind()` after assigning these fields.
It follows the full bone affine and animated draw order in Play mode. Local TRS,
anchors and offsets are retained; stretched content continues to use its original
parent's rect size and pivot. `FollowAttachment` has the same visibility behavior
as scene slot objects. Disabling the graphic hides the bound content.

Children using `UI/Default` inherit slot/skeleton opacity, the host Graphic's
alpha and Core clipping. Native CanvasGroup opacity, Mask, RectMask2D softness
and UI raycasts continue to apply; pointer hits outside the Core clip are rejected.
The optional slot shader requires SM4.5. Custom shaders use the same slot-mask
properties/include as scene objects and must also implement native UI rendering.
Shaders without those properties retain their original appearance.

Binding clones compatible materials and restores them and the original parent
on release. Rebinding the same RectTransform transfers ownership. Call `Rebind()`
after changing the slot, content hierarchy or child materials. Layout and animation
inside the content remain native Unity UI behavior. In manual mode, finish layout
changes before calling `CaneGraphic.Upload()`.

## Color and geometry

Cane world positions are Y-up, matching Unity's XY plane. UVs convert once from
Cane's top-left convention. The adapter does not solve bones or rebuild geometry.
Vertex tint remains floating point. Index buffers are reused when topology is
unchanged; mesh capacity grows only when needed.

The Unity project's Gamma/Linear setting chooses the composition space. V2 PMA
textures associate RGB with alpha in their declared storage space. When conversion
is required, resources unpremultiply, transfer and premultiply per texel before
filtering, retaining the converted texture. A Gamma project can use an sRGB PMA
texture directly. Original exported files are unchanged.
Exported min/mag/mip filtering and per-axis wrapping are applied without changing
borrowed textures. Independent min/mag and nearest-texel/linear-mip combinations
use a shader sampling variant; common combinations use the native sampler.

## Built-in lighting

`Cane/Lit` is an optional scene material for Unity's Built-in Forward renderer.
Enable `CaneRenderer.AddNormals` for lighting, and `SolveTangents` for normal maps
(it also enables normals). These attributes are separate optional mesh streams;
ordinary unlit rendering keeps its existing vertex layout. Normals are constant
in the mesh's XY plane. Tangents follow final deformed positions and atlas UVs,
including rotated regions and reflected geometry. Separators retain these streams
and the source MeshRenderer's shadow/probe settings.

Create a material with `Cane/Lit`, assign its prepared texture from
`CaneResources.Textures`, and set `_Pma` to match the exported texture's alpha mode.
Assign the material through `CaneRenderer.SlotMaterials`. When copying an existing
resource material and changing its shader, retain its texture, `_Pma` and sampling
settings. Keep a serialized material/shader reference so Unity includes it in the
Player; `Shader.Find` alone is not a build reference. Unused lighting shaders are
not forced into an unlit Player.

The material supports directional, point and spot lights, normal strength,
metallic/smoothness maps and emission, with Core's two-color tint and opacity.
Opacity attenuates the whole lit result, including specular and emission. Normal
alpha blending is provided; this material does not replace the existing unlit
additive/multiply/screen materials. It is not a Canvas material.

Both `Cane/Skeleton` and `Cane/Lit` can cast realtime shadows; `_Cutoff` sets the
texture-times-vertex-alpha threshold. Use native MeshRenderer shadow controls.
For directional shadow receiving in the Built-in pipeline, enable `_ZWrite` and
set the receiving material's Render Queue to `2450` (AlphaTest), so it participates
in the depth prepass. Depth writing changes overlap/edge behavior; use appropriate
`ZSpacing` when layering parts. Ordinary transparent rendering defaults to no
depth writing.

The optional [URP package](../cane-unity-urp/README.md) supplies separate Universal
Renderer and 2D Renderer shaders through `CaneAsset.SceneShader` or the
`CaneResources` constructor. Canvas retains its existing shader. The Built-in
lighting shader does not support URP or HDRP. The optional material effects
described above share the same geometry and resource lifecycle.
