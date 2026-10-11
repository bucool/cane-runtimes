# Cane version and capability contract

Format, behavior, package and conformance versions are independent.

| Domain | Marker | Current version |
| --- | --- | --- |
| Authoring project | `cane-project` | 1.1 |
| Runtime JSON | `cane-runtime` | 1.1 |
| CANEB | CANEB magic | 1.1 |
| Atlas JSON | `cane-atlas` | 1.0 |
| Runtime API | `cane.runtime/v1` | 1.13 |
| Conformance suite | `cane.runtime-conformance/*/v1` | 1.17.0 |

Runtime JSON and CANEB readers accept container 1.1; Atlas readers accept 1.0. Container 1.0
is not an implicit alias for Runtime 1.1. Unknown major/minor values outside the advertised
format range are rejected. Numeric selector tags and serialized names follow the 1.1 format
specifications. Import or conversion is a separate tooling operation.

## Behavior profiles

A document's `runtimeApiVersion` describes its minimum behavior requirement, not its container
version. Supported behavior declarations are 1.0 and 1.4 through 1.13. API 1.1–1.3 host query
facilities remain available; those values are not accepted as native document behavior selectors.

| Required document feature | Minimum behavior minor | Capability bit | Specification |
| --- | --- | --- | --- |
| `constraint.physics.spring-v2` | 4 | 42 | [Spring dynamics](SPRING_DYNAMICS_DESIGN_V2.md) |
| `constraint.path.geometry-v2` | 5 | 43 | [Path geometry](PATH_GEOMETRY_DESIGN_V2.md) |
| `constraint.ik.affine-v2` | 6 | 44 | [Affine IK](IK_AFFINE_DESIGN_V2.md) |
| `constraint.transform.channels-v2` | 7 | 45 | [Transform channels](TRANSFORM_CHANNELS_DESIGN_V2.md) |
| `bone.inheritance.polar-v2` | 8 | 46 | [Polar inheritance](INHERITANCE_POLAR_DESIGN_V2.md) |
| `animation.curve.roots-v2` | 9 | 49 | [Cubic curves](ANIMATION_CURVE_ROOTS_DESIGN_V2.md) |
| `animation.track.pose-v2` | 10 | 50 | [Pose composition](TRACK_POSE_COMPOSITION_DESIGN_V2.md) |
| `animation.sequence.clock-v2` | 11 | 51 | [Sequence clocks](SEQUENCE_CLOCK_DESIGN_V2.md) |
| `mesh.placement` | 13 | 52 | [Mesh placement](MESH_PLACEMENT_V1.md) |

Each used profile requires both its feature declaration and its minimum behavior minor.
All profiles used by one document remain active together. A higher minor does not disable
earlier features or select an alternate solver. Missing profiles, unknown required features,
unknown modes and unsupported required binary sections are errors before player allocation.

The polar profile is required by setup or animated `IgnoreRotationAndReflection`, `IgnoreScale`
and `IgnoreScaleAndReflection`. `InheritAll` and `TranslationOnly` alone do not require it.
Continuous cubic channels require roots-v2; unused curves on discrete attachment switches do not.
Advancing sequence keys require clock-v2. Load validation scans all declarations, including
animations not currently selected for playback.

## Playback API 1.12

`AnimationPlayer` owns `AnimationPlayback` entries. Playback fields use `blendWeight`,
`playbackRate`, `playbackTime`, animation ranges, blend durations/elapsed time and explicit
blend thresholds, with language-native casing and unit suffixes. Public operations include
`playAnimation`, `fadeOutTrack`, `queueFadeOut`, `stopTrack`, `stopAllTracks`, `advanceTime`,
`setPlaybackTime` and `setBlendDuration`. Image alpha, Slider time mapping and constraint mix
retain their separate meanings. See [playback names](../../docs/PLAYBACK_API.md).

Lifecycle events are `playbackStarted`, `playbackInterrupted`, `playbackEnded`,
`playbackRemoved`, `playbackCompleted` and `customEvent`. Native file mode names are specified
by [Runtime Model](../formats/RUNTIME_MODEL_V1.md). Retired playback names are not compatibility
aliases in this API revision. Pre-release package source users must update call sites.

## Host-only capabilities

Capabilities 17–41 describe owned queries, source geometry, authoring overrides, project
reconciliation, sampling controls, inverse geometry queries, transient pose modifiers,
instance resource overlays, bounds, Physics host motion and final geometry modifiers.
Bits 47/48 describe the owned setup-bone frame and checked inverse queries. They add no
renderer types, editor commands or serialized project state. Hosts discover each advertised
capability instead of inferring it from a package or file version.

## Required features and errors

`requiredFeatures` is a sorted, duplicate-free array of dotted ASCII feature names. A loader
must not silently approximate, ignore or partially load a required feature. Optional fields
may be skipped only where the containing format object explicitly permits extensions.
Optional CANEB sections may be skipped inside a supported container version; required
unknown sections may not. IDs remain case-sensitive opaque UTF-8 strings and declaration
order remains observable where specified.

## Conformance identity

Implementations report supported format ranges, API version, feature names/bits and the exact
last-passed suite version and manifest digest. Current suite 1.17.0 contains 391 required cases;
its digest is `f462e113115ecd60322f6f5d9df451e05d82649d0ded3278137f4b873e3ce0f9`.
A passing declaration requires two runs from fresh state with deterministic fingerprints.
It covers the normalized CPU contract, not engine pixels or GUI performance.

## Mesh placement API 1.13

Nonzero displayed-mesh placement requires API 1.13 and `mesh.placement`. Zero or
absent placement preserves prior minimums. Frame/RenderPacket and container
formats are unchanged. The frozen 391-case Suite 1.17.0 keeps its original
identity; placement extension verification is recorded separately.
