# Cane C# Core

Native, renderer-independent .NET Standard 2.1 implementation of the current
[Cane v2 contract](../../specification/runtime/RUNTIME_V2.md).
It has no Unity, Rust, native interop or third-party dependencies.

Reference `Cane.Core.csproj` from a .NET application, or add this directory's
`package.json` through Unity Package Manager. Use the adjacent
[Unity adapter](../cane-unity/README.md) for scene and Canvas rendering.

```csharp
using Cane;

RuntimeAsset asset = RuntimeAsset.Load(documentBytes); // Existing JSON or .cane2.
var skeleton = new Skeleton(asset.Data);
var state = new AnimationState();
state.Set(0, asset.Animations[0], loop: true);

void Update(double seconds)
{
    state.Update(seconds);
    state.Apply(skeleton);
    // Game code can edit indexed pose/constraint channels here.
    skeleton.UpdateWorld(seconds);
    skeleton.UpdateGeometry();

    foreach (var draw in skeleton.Frame.Draws)
    {
        // Upload only VertexCount / IndexCount elements. Vertices, UVs, tint,
        // clipping and order are already final; the renderer must not solve again.
    }
    foreach (var e in state.Events) System.Console.WriteLine(e.Type);
    state.Events.Clear();
}
```

`RuntimeAsset` holds shared compiled animation, texture descriptors and lookup
tables. Treat these arrays as read-only. Each `Skeleton` and `AnimationState`
owns mutable playback state and reusable output buffers. Draws are borrowed until
the next evaluation; copy them only when a retained snapshot is needed. A skeleton
without appearance data has a null `Frame`.

`RuntimeAsset` exposes bone, slot, attachment, skin, constraint and animation IDs
in export order. `Skeleton.Pose` contains indexed local channels; `World` contains
six doubles per bone in `[a,b,c,d,x,y]` order. `Applied` recovers constrained local
values on demand. Runtime math is double precision; final GPU geometry is float32.

`AnimationState.Set` interrupts a track, `Add` queues an entry, and `Clear` releases
it. `TrackEntry` controls time, range, speed, alpha, looping, reverse, additive
blending, thresholds and mixing. `Seek` suppresses crossed events unless explicitly
requested. Empty entries mix back to setup. Consume lifecycle and keyed events in
order after evaluation; the Core invokes no application callbacks.

`SetBlend(track, animations)` creates simultaneous inputs for an external controller
or blend tree. Configure it when membership changes, then update each returned
entry's `Alpha` and `Time` (with `Speed = 0` for an external clock). Replacement
weights above one are normalized; unused weight retains the lower layer/setup.
The strongest input controls discrete properties. Additive inputs remain relative
to setup. Zero-weight inputs consume their event interval silently. Use `Set` to
return that track to normal queued playback.

Assign a `RootMotion(data, boneIndex)` to `state.Motion` to extract local X/Y
translation from its `Tracks` (an empty array selects all tracks). After `Update`,
consume `DeltaX/Y`. `PoseX/Y` contain the setup translation blended with unselected
tracks; use them for the extracted axes before solving the skeleton. Extraction
alone does not modify the pose or move an engine object. A `Seek` suppresses root
movement as well as crossed events unless `emitEvents` is requested.

Translation uses unwrapped clip intervals, so loop boundaries and different clip
starting offsets do not teleport the host. Queue/delay activation splits the frame
into intervals; each uses its ending mix weights and actual curve samples on all
selected tracks. This is discrete frame sampling, not analytic integration of a
changing blend curve. Animator/graph callers can drive entry time with `Speed = 0`.
For continuous reverse movement, use `TrackEntry.Reverse` or a negative entry
`Speed` while passing nonnegative elapsed time to `Update`.

`motion.Translation(entry, from, to)` queries the bone's unweighted local
displacement between elapsed entry times. It includes ranges, reverse and loops,
uses setup values before the first key, and does not seek or change player state.
It queries that entry only; future track mixing and constraints are not predicted.

`SetSkins` combines skins in order, with later selections taking precedence.
`AddAttachment`, `Frame.SetAttachment` and `Frame.Deform` support instance-owned
appearance changes. `AtlasRegion.CreateAttachment` constructs a region mesh using
trim and rotation metadata. Point, path, bounds, hit/segment and coordinate queries
read evaluated state without running another solver.

`MeshAttachment.RemapTextures` creates a copy with different texture indices and
UVs, including every sequence image. Geometry, triangle indices and sequence
timeline identity remain shared, so existing deformation and sequence timelines
continue to work. Tint is copied. `Skeleton.ReplaceAttachment(index, attachment)`
changes that instance without modifying the shared asset or skin bindings.
`TextureRegion.Bounds` and `RemapUvs` describe integer texel placement in Cane's
top-left texture coordinates. `Union`, `Align` and `Transform` calculate the
covering texel rectangle, including negative coordinates and reflected texture
transforms. Transform offsets are in texels. These operations do not resize
images or alter geometry.

`Skeleton.SlotOpacity(slot)` returns slot alpha multiplied by skeleton alpha,
without attachment alpha, for external objects. `Frame.Clipping(slot, before,
boundary)` resolves their active clip from evaluated draw order. Reuse a
`ClippingBoundary`; its `Vertices`, `Triangles` and `Inverse` expose the same
partition used for final geometry. `ContainsPoint(x, y)` queries that partition,
including inverse clips. The spans are borrowed until the next frame evaluation
or boundary query. Engine adapters apply this mask to their own objects rather
than retriangulating the contour with different fill rules.

The Core implements bone inheritance, IK, mapped Transform, Path, Physics and
Slider constraints, layered animation, deformation, sequences, clipping and
light/dark tint. Scheduling is prepared at load time. Engine lifecycle, textures,
materials, scene objects, physics bodies and editor UI belong in an adapter.

For external bone inputs, register a reusable `BoneControl` with
`Skeleton.SetBoneControl(bone, control)`. Fill its `[a,b,c,d,x,y]` matrix in skeleton
world coordinates, or set `World = false` for bone-parent local coordinates.
Position, rotation, scale and shear have independent mix weights. Core resolves
the input against the current parent basis once before constraints; constraints
can subsequently change that bone or its ancestors. A singular inverse leaves
the original local pose in place. `Pose` remains the animation/host baseline and
`Applied` exposes the evaluated pose. Pass null to release a control. Binding and
skin changes rebuild the dependency schedule; updating a bound matrix does not.
