# Unreal API

All player access takes place on the game thread. Every scene component or UMG
widget owns a separate player; immutable source data can be shared by an asset.
The wrappers report errors through `LastError`, boolean results and the player's
`OnError` event. A host pauses after a ticking/render-resource failure.

## Scene and UMG

Place a `CaneActor`, select its `Cane` component and assign the imported asset.
Choose `InitialAnimation` using the asset's animation **ID**. The component maps
Cane X-right/Y-up coordinates to Unreal X/Z exactly once. `UnitsPerCaneUnit`
controls physical size; the Actor transform supplies world placement.

`BlendColorSpace = Gamma` is the default on the scene component, UMG widget and
isolated surface. Choose `Linear` for linear-light filtering, tinting and blending.
This is independent of the source texture's encoding and the Slate target's
`TargetColorSpace`. Both choices support normal, add, multiply and screen.
Call `SetBlendColorSpace(ECaneBlendColorSpace::Linear)` (or the Blueprint property
setter) to switch a component/widget immediately, including while paused. This
reuses the current frame without changing tracks or playback time. For direct
`UCaneSurface` use, assign the property before the next `Draw`.

`Rendering = DirectScene` is the default. It uses final Core triangles in the
scene-color pass; it does not crop them to the asset's surface rectangle.
`Rendering = IsolatedSurface` uses one textured quad and the asset's fixed surface
extent/resolution. Reinitialize after changing the rendering mode. See
[rendering limits](RENDERING.md) for depth, translucency and post-processing behavior.

`bPaused`, `PlaybackRate` and `bLoop` control playback. `bLoop` supplies the initial
Play call; use `Player.SetTrackOptions` to change a running track. Editor animation
requires `bPreviewInEditor`; a normal registration shows the initial pose.
`SetSkeleton(nullptr)` clears the scene component's player, surface and submitted
geometry; assigning another valid asset initializes a fresh player.

Add `Cane Animation` (`UCaneWidget`) to UMG and assign the same asset. Its independent
player ticks while Slate ticks the widget; the designer displays the initial pose.
Its default `Rendering = DirectSlate` submits final packet triangles at the widget's
Slate layer. Earlier widgets in that render target participate in all four blend
modes. Layout, rotated/reflected render transforms, inherited opacity and parent
clipping are applied after Core publishes its geometry. No second animation
evaluator exists. The asset's surface rectangle defines the widget framing.

`Rendering = IsolatedSurface` keeps the AlphaComposite UI material presentation.
Reinitialize after switching modes. `TargetColorSpace = Auto` treats ordinary
8-bit Slate/retainer targets as gamma encoded and float targets as linear. An
application using a custom `FWidgetRenderer` gamma policy can explicitly select
`Linear` or `SlateSRGB`. Native HDR display planes are currently rejected; see
[rendering limits](RENDERING.md). `SetSkeleton(nullptr)` clears the widget and player.

## Blueprint player operations

| Operation | Behavior |
| --- | --- |
| `Initialize(Asset)` | Prepare a new player; invalid data retains the old one |
| `Play(Id, Track, Loop, MixSeconds)` | Select an animation with an explicit transition |
| `Queue(Id, Track, Loop, DelaySeconds, MixSeconds)` | Queue the next animation |
| `Stop`, `Fade`, `Reset` | Remove a track, fade it, or reset the player |
| `Advance(DeltaSeconds)` | Advance and publish once; normally called by the host |
| `Seek(Seconds, FixedStep)` | Replay from Core's configuration baseline |
| `SetTrackOptions` / `GetTrack` | Weight, rate, loop and current playback state |
| `SetSkins` | Install the ordered skin selection |
| `SetAttachment` | Override a slot; an empty attachment ID hides it |
| `RestoreAttachment` | Return a slot to its authored/animated selection |
| `SetRoot`, `ResetPhysics` | Host pose or explicit physics reset |
| `GetBone` | Read the current full affine origin/axes and activity |

The host already advances its player. Do not also call `Advance` unless the host
is paused, or time advances twice. Moving the Actor changes its scene placement;
use Core root/physics controls when movement should affect skeletal inertia.

`OnFrame` signals the published frame. `OnAnimationEvent` delivers playback
lifecycle and custom events, including optional integer/text/float/audio payloads.
Core events are drained once, not once per renderer. Seek also delivers its replay
event batch. `AudioId` is an event payload; audio playback belongs to the game.

Mutating a player from its own frame/event delegate is rejected. Schedule that
mutation for the next game tick. Read-only bone/track queries are allowed. This
prevents callbacks from invalidating the frame/event batch currently being delivered.

## C++ access

`GetNativePlayer()` returns a game-thread-only read-only Core pointer.
`GetNativeFrame()` returns the current owned publication; copy the `RuntimeFrame`
if it must survive the next update. Do not retain a raw frame/player pointer across
reinitialization. Full Core queries remain available, including constraints,
geometry, bounds, slots and resource catalogs.

```cpp
#include "CanePlayer.h"
#include <cane/runtime_player.hpp>

Player->EditNative([](cane::RuntimePlayer& Native) {
    Native.set_blend_duration("idle", "walk", 0.15f);
});
```

The callback executes synchronously, then publishes once. Core operations retain
their native transaction guarantees; a series of operations in a user callback
is not one combined transaction. Do not retain the mutable reference, advance the
clock again, or call back into the UObject from the callback. Host APIs which
replace runtime textures require corresponding UE resource binding; arbitrary
new texture resources are not supported by the current imported-asset surface.

## Bone anchors

`UCaneBoneAnchorComponent` follows a selected bone's position and transforms
`LocalOffset` by its full published affine. `BoneAxisX`/`BoneAxisY` and
`GetBoneWorldAxes` retain reflection, nonuniform scale and shear. The anchor does
not manufacture a rotation/scale approximation: Unreal `FTransform` cannot store
a general 2D shear. The game can use the returned axes to orient custom geometry.
