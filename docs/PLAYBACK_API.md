# Playback API

The current contract is Runtime API 1.12, Runtime JSON/CANEB 1.1 and Atlas JSON 1.0.
The native file vocabulary uses the inheritance and sequence names below. Re-export
assets from the current Cane editor before loading them with this release.

## Playback vocabulary

`AnimationPlayer` manages tracks. `AnimationPlayback` is one current, outgoing or
queued animation. Language bindings use their native casing and may add `Seconds`
to explicitly dimensional fields.

| Meaning | Name |
| --- | --- |
| Animation interval | `animationRangeStart`, `animationRangeEnd` |
| Entry clock and scheduled start | `playbackTime`, `startDelay` |
| Entry stop and speed | `playbackEnd`, `playbackRate` |
| Entry contribution | `blendWeight` |
| Transition clock | `blendDuration`, `blendElapsed`, `blendProgress` |
| Discrete-channel gates | `eventBlendThreshold`, `attachmentBlendThreshold`, `drawOrderBlendThreshold` |
| Outgoing pose policy | `holdPreviousPose`, `blendSource` |

Playback controls are `playAnimation`, `fadeOutTrack`, `queueFadeOut`, `stopTrack`,
`stopAllTracks`, `advanceTime`, `clearPendingEvents` and `setBlendDuration`.
`advanceTime` changes clocks and collects events; `apply` evaluates a frame;
`advance` performs both. Image/tint opacity remains `alpha`. Slider time mapping
retains `timeScale`, and constraint influence retains its `mix` controls.

Events are `playbackStarted`, `playbackInterrupted`, `playbackEnded`,
`playbackRemoved`, `playbackCompleted` and `customEvent`.

Bone inheritance selectors are `inheritAll`, `translationOnly`,
`ignoreRotationAndReflection`, `ignoreScale`, `ignoreScaleAndReflection`.
Sequence selectors are `fixedFrame`, `playOnce`, `repeat`, `backAndForthLoop`,
`playOnceReverse`, `repeatReverse`, `backAndForthLoopReverse`.

## Evaluation

Every track reads an immutable pose from lower tracks. Each reachable endpoint
is sampled against that pose; transition progress composes the endpoints.
Mixing weights, additive differences, angles, bounded values and discrete
selection use the channel rules in the [pose specification](../specification/runtime/TRACK_POSE_COMPOSITION_DESIGN_V2.md).
Zero-weight presence and missing channels follow the same rules in every Core.

Curve roots, inherited affine bases, IK, transform channels, path geometry,
analytic spring evolution and sequence clocks are defined in the
[specification](../specification/README.md). Engine adapters consume the resulting
owned world geometry, UVs, tint and ordering without evaluating a second pose.

## Setup queries

`RuntimePlayer.setupBoneFrame` / `RuntimePlayer.SetupBoneFrame` evaluates an input
list of `{id, parentId, local, transformMode}` bones and returns an owned frame.
The C++ projection `RuntimePlayer::setup_bone_frame` returns its owned bone array.
Input order is preserved, parents may appear later in the input, and duplicate IDs,
missing parents and cycles are rejected. There are no animations, constraints,
attachments, skin filters or host transforms in this query.

`solveBoneLocalForWorld` / `SolveBoneLocalForWorld` / `solve_bone_local_for_world`
recovers a local pose from a requested world matrix, optional parent matrix,
inheritance mode and reference local pose. A null parent uses ordinary root
composition. Non-finite arguments are rejected; an unreachable or unrepresentable
world pose produces `invalidState`. Singular parents are handled by the same
checked inverse used by Core transform evaluation.
