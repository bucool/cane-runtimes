# Track pose composition v2 — independent candidate

Runtime API 1.10 and `animation.track.pose-v2` select this endpoint composition contract.

## Pose and clip boundary

A pose is an ordered set of typed local authoring channels, before constraints and world
geometry. The setup pose S defines the schema. A sampled clip has one optional value per channel,
opacity a in [0,1], and Replace or Additive mode. Absence means that clip supplies no value at the
sample time. A clip with no value yet does not gain ownership merely because it has a later key.
Paired/scalar key precedence must be resolved by the sampler once, before composition.

Channel types:

- Scalar: finite f32 coordinates, scales, unbounded parameters.
- Bounded scalar: finite interval [lo,hi], also applied after each clip operation. Color components
  and opacity can use this type; general constraint coefficients must not be indiscriminately clamped.
- Angle: degrees. Replace and transition interpolation take the shortest signed difference in
  [-180,180), selecting -180 at a tie. Exact opacity/progress endpoints retain the exact supplied
  endpoint representation. Additive uses that same shortest difference from setup. The direction
  convention and setup-relative angle delta retain the existing choice, not a new multi-turn mode.
- Vector: finite f32 elements in a **canonical** coordinate space, with an equal layout identity
  and length. The identity includes attachment/topology and influence order as applicable. Mismatched
  deform layouts are an error, never silently replaced, truncated or padded by the composer.
- Discrete: an opaque interned value, covering attachment/skin choices, inheritance mode, draw order
  and similar states. A present value replaces the underlay at opacity >= 1/2 in either blend mode.
  The composer does not interpret IDs, sample timelines, or solve geometry.

Missing channels are identities, not setup resets. Using the same underlay pose L for both endpoints:

```
clip_replace(L, C, a) = (1-a)L + aC
clip_additive(L, C, a) = L + a(C-S)
clip_missing(L) = L
```

Apply these equations componentwise, with the type policies above. Bounded results saturate only
after the wide arithmetic, while ordinary scalar/vector/angle outputs outside finite f32 fail.
All arithmetic intermediates use f64; only complete channel operations narrow to f32. Schema and
finite-input validation precede evaluation, including inactive supplied samples. A future sampler
must avoid unnecessarily sampling disabled curves; this kernel does not manufacture sample values.

## Transition and stack

Each track is a chain of nodes. A leaf contains a sampled clip; a transition contains an earlier
node O, a new sampled clip I, progress m in [0,1] and `hold_missing`. Node links point strictly
backward, so cycles are invalid. Multiple tracks may refer to a node; track order remains explicit.
This is a pose evaluation graph, separate from the existing queue, clocks and lifecycle events.

For a transition, evaluate O over L, and separately apply I over **the same L**:

```
Pout = evaluate(O, L)
Pin  = clip(L, I)
P    = interpolate(Pout, Pin, m)
```

Use shortest-angle interpolation and the discrete winner rule (outgoing below 1/2, incoming at
or above 1/2). Linear/vector/bounded channels use convex interpolation. At m=0 return Pout without
evaluating the incoming operation; at m=1 evaluate I over L without evaluating O. An unused
endpoint's possible arithmetic overflow cannot fail evaluation, but malformed input/schema still can.

With `hold_missing`, for each channel absent from I, set Pin to Pout during 0<m<1. This explicitly
holds missing channels during the transition. At m=1 the previous node is released and the clip
uses L again; that policy has an intentional endpoint jump for held channels. It does not retain
hidden previous clips forever. A present but zero-opacity channel still counts as present for this
explicit option; without hold, all zero-opacity clips are identities.

An empty incoming clip is simply missing all channels: ordinary transition fades to L; explicit
hold retains Pout until completion. Interrupted A→B→C evaluates A→B at its current progress over L,
then interpolates that result against C(L). No search for property owners in lower tracks is needed.
The completed pose of one track is L for the next track.

### Hand-derived examples

Setup X=0, outgoing clip X=10 with a=1/4, incoming X=30 with a=1/2, m=1/2:
`Pout=2.5`, `Pin=15`, result **8.75**. Both endpoints are evaluated against the same underlay before interpolation.

At full opacity the same transition gives 20. A lower clip X=4 with opacity 0 produces L=0 and
must still give 20; its mere existence cannot change this answer. If incoming lacks Y and outgoing
Y=20, halfway gives 10 normally and 20 with hold. At completion both use underlay Y=0.

A→B halfway, then that result→C by 1/4, with X values 10,30,50, gives
`(1-1/4)((1-1/2)10+(1/2)30)+(1/4)50 = 27.5`.
The current per-entry interrupted-chain policy also gives 27.5 in this full-opacity example;
not every case changes. Partial opacity and multiple tracks require separate comparisons.

For scalar Replace, the result is an affine combination of setup and sampled values. Expanding
coefficients symbolically using rational opacities/progress yields an oracle independent of the
candidate's pose-first evaluation. Additive adds setup-relative coefficients. This oracle applies
only to linear channels; it is not used to certify angle, saturation, discrete or deform policies.

## Failure and work bounds

Return an owned pose on success. No caller-supplied pose, sample or graph is mutated on failure.
Reject non-finite inputs, out-of-range opacity/progress, wrong channel types/bounds/layouts,
invalid references/cycles and nonrepresentable outputs explicitly. Validate and evaluate under
one caller-supplied work budget, counting node/reference visits, channel headers/elements and
pose copies. Exhaustion returns a distinct error, not a partial pose or silently reduced result.
Use an iterative backward-chain walk, avoiding recursive stack growth. The shared operation budget also covers preparation, projection and publication; see
[sampling work](TRACK_SAMPLING_WORK_V2.md).

## Integration requirements

Apply all typed channels, discrete gates, zero-weight presence, ordered skin/draw-order operations,
and Physics reset selection before atomic publication. Queue timing and events retain their own
clock semantics. Public operations propagate structured errors without partial state changes.
See [channel projection](TRACK_CHANNEL_PROJECTION_V2.md), [entry evaluation](TRACK_ENTRY_EVALUATION_V2.md)
and [versioning](VERSIONING.md). Conformance covers numeric behavior; GPU/GUI performance is a
separate host measurement.
