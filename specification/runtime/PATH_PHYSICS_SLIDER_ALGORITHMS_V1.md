# Cane Runtime v1 Path, Physics, and Slider Constraint Algorithms

Status: behavioral specification. Execution evidence is recorded by each implementation.
Slider source mapping retains its API 1.0 contract; its animation composition uses pose-v2
at API 1.10. Physics spring-v2 requires API 1.4.

This document defines the language-neutral Path, Physics, and Slider
constraint algorithms. Together with the
[Runtime Model](../formats/RUNTIME_MODEL_V1.md),
[Runtime API](RUNTIME_API_V1.md),
[Core Animation Algorithms](CORE_ANIMATION_ALGORITHMS_V1.md), and
[Geometry and Render Algorithms](GEOMETRY_RENDER_ALGORITHMS_V1.md), it is
sufficient to implement these three constraint families without consulting
the Rust reference implementation.

The words **MUST**, **MUST NOT**, **SHOULD**, and **MAY** are normative.

## 1. Shared numeric, activation, and ordering rules

The numeric and affine conventions in Core Animation Algorithms v1 apply:

- time is seconds, angles are degrees, positions are `px`;
- the reference scalar is IEEE 754 binary32;
- all persisted and operation-input numbers are finite;
- expressions are evaluated in the written order;
- the complete six-component affine matrix is authoritative;
- `euclideanRemainder(x,m)` is in `[0,m)` for positive `m`;
- `wrapDegrees(x) = euclideanRemainder(x + 180, 360) - 180`.

All zero, sign, range, and degeneracy decisions in this document are exact
binary32 comparisons. An implementation MUST NOT substitute an epsilon for
any stated comparison. In particular:

- the smallest positive binary32 duration is positive;
- the smallest positive binary32 Path or Physics mix is active;
- the smallest positive or negative binary32 Slider mix is active;
- a determinant is singular only when it is exactly zero or non-finite;
- `+0` and `-0` compare equal and both take the zero branch.

Conformance tolerances apply only when comparing published numbers. They do
not alter an algorithm branch.

At the beginning of `apply`, setup constraints are copied into mutable sampled
constraints. Animation layers and host overrides modify that sampled copy.
Constraints are then visited exactly once in document declaration order.
After a constraint modifies a bone, the implementation rebuilds the affected
local pose and propagates world transforms to its descendants. A multi-bone
Path constraint is one atomic constraint operation: every driven bone reads
the same world-state snapshot captured before that Path constraint, then the
staged commit rule in section 2.8 publishes all desired worlds without one
driven bone contaminating another. Consequently, every later constraint
observes the complete final result of every earlier constraint.

A constraint listed by one or more skins is active only when at least one
sampled active skin lists it. A member listed by no skin is always active.
Additional family-specific activation rules are stated below. An inactive or
unresolvable constraint is a deterministic no-op and produces no diagnostic.
Validated Runtime Format v1 data prevents unresolved setup references; the
no-op rules cover runtime attachment/skin selection and defensive API
implementations.

## 2. Path attachments and Path constraints

This section is the legacy `constraint.path` contract. Current players reject that required
feature until explicitly migrated. API 1.5 uses bounded arc subdivision and spatial projection,
stationary-curve/endpoint derivative fallbacks, independent Chain anchors, and explicit numeric
or resource errors, as specified in [Path geometry v2](PATH_GEOMETRY_DESIGN_V2.md).

### 2.1 Cubic control-point layout

A Path attachment contains `N >= 2` knots. Each knot has exactly three
two-dimensional control points in this order:

```text
knot[i] = [incomingHandle[i], anchor[i], outgoingHandle[i]]
```

The flattened `vertices` array therefore has six numbers per knot:

```text
[
  incomingX, incomingY,
  anchorX,   anchorY,
  outgoingX, outgoingY,
  ...
]
```

For curve `i`, with `next = (i + 1) mod N`:

```text
p0 = anchor[i]
p1 = outgoingHandle[i]
p2 = incomingHandle[next]
p3 = anchor[next]
```

An open Path has `N - 1` curves. A closed Path has `N` curves and the final
curve connects the last knot to the first.

The cubic point at `t in [0,1]` is:

```text
u = 1 - t
B(t) =
    u*u*u*p0
  + 3*u*u*t*p1
  + 3*u*t*t*p2
  + t*t*t*p3
```

Its tangent is:

```text
dx =
    3*u*u*(p1.x - p0.x)
  + 6*u*t*(p2.x - p1.x)
  + 3*t*t*(p3.x - p2.x)

dy =
    3*u*u*(p1.y - p0.y)
  + 6*u*t*(p2.y - p1.y)
  + 3*t*t*(p3.y - p2.y)
```

When `dx != 0 || dy != 0`, the tangent angle is
`atan2(dy,dx)` converted to degrees. When the derivative is exactly zero, the
setup-length sampler falls back to the angle from `p0` to `p3`.

### 2.2 World control points, weights, and deform

The Path constraint resolves the target slot's currently selected attachment
after animation, skin, attachment override, and deform sampling. If that
attachment is not a Path, the constraint is a no-op.

World control points use the vertex-attachment algorithm in
Geometry and Render Algorithms v1:

- an unweighted point is transformed by the target slot bone's full world
  matrix;
- a weighted point is the normalized positive-influence sum using the current
  full world matrices and either explicit influence-local coordinates or the
  declared bind inverse;
- `vertexPositions` deform replaces each setup point before projection;
- `weightedInfluenceOffsets` deform offsets each influence-local coordinate
  before weighted projection;
- malformed rows, missing bind data, a zero positive-weight sum, or non-finite
  output make Path resolution fail rather than inventing a fallback transform.

The Path solver operates on these final world control points. It MUST NOT run
a second skinning or deform formula of its own.

### 2.3 Constant-speed sampler

When `constantSpeed == true`, each current world cubic first receives a coarse
length by subdividing it into exactly four equal parameter intervals:

```text
t[j] = j / 4, j = 0..4
```

The four Euclidean segment lengths are accumulated in binary32. Curve coarse
lengths are then accumulated in declaration order; their final cumulative
value is `totalLength` and selects which cubic owns an input distance.

Independently, every curve is subdivided into exactly ten equal parameter
intervals. The ten Euclidean segment lengths form a curve-local binary32
cumulative lookup. For an in-range distance, select the first coarse curve end
greater than or equal to the distance. Convert the distance to coarse curve
progress and then to a target in the ten-segment lookup:

```text
curveProgress = (distance - curveStart) / coarseCurveLength
target = curveProgress * detailedCurveLength
segment = first detailed cumulative length >= target
segmentProgress = (target - segmentStart) / segmentLength
t = (segmentIndex + segmentProgress) / 10
point = B(t)
tangent = angle(B'(t))
```

All divisions use ratio zero when their denominator is exactly zero, and
ratios are clamped to `[0,1]`. The final point is evaluated on the cubic; it is
not a linear interpolation between lookup samples. A zero derivative uses the
`p0` to `p3` fallback from section 2.1.

The sampler exists only when `totalLength > 0`. Zero-length segments remain
in either cumulative table; they are not expanded by an epsilon.

### 2.4 Setup-length sampler

When `constantSpeed == false`, each current world curve is sampled directly,
but distance-to-curve parameterization comes from cumulative Path lengths.

An authoritative `lengths` array is used when:

1. its count is exactly the Path knot count;
2. every value is finite;
3. values are non-negative and non-decreasing.

Only the first `curveCount` cumulative values parameterize curves. For an open
Path, the final knot entry is format padding and is not a separate curve
length. If the authoritative array is absent, the implementation computes a
deterministic fallback from the current world control points by the same
24-subdivision cumulative-length algorithm used by format tooling.

The sampler exists only when the last used cumulative length is greater than
zero. Equal adjacent cumulative values are legal and create a zero-length
parameter interval.

For an in-range distance, select the first cumulative curve end greater than
or equal to the distance. Let `previousLength` be zero for the first curve and
the prior cumulative entry otherwise:

```text
curveLength = curveEnd - previousLength
t = 0                                      if curveLength == 0
t = clamp((distance-previousLength)
          / curveLength, 0, 1)             otherwise
point = B(t)
```

This mode does not reparameterize the current cubic by current geometric arc
length. Setup lengths select a curve and linearly select that curve's `t`.

### 2.5 Closed wrapping and open extrapolation

For a closed Path:

```text
distance = euclideanRemainder(distance, totalLength)
```

This applies to negative and positive distances. Therefore sampling exactly
`totalLength` returns the start point.

For an open Path, an in-range endpoint is owned by the Path. Extrapolation in
both modes uses the cubic's first anchor/outgoing-handle segment or final
incoming-handle/anchor segment.

Let `startFrom` be the first outgoing handle. A distance below zero
extrapolates from the first anchor toward the opposite direction:

```text
direction = normalize(firstAnchor - startFrom)
point = firstAnchor + direction * (-distance)
```

Let `endFrom` be the final incoming handle. A distance above `totalLength`
extrapolates from the final anchor in the continuing direction:

```text
direction = normalize(finalAnchor - endFrom)
point = finalAnchor + direction * (distance - totalLength)
```

If the applicable endpoint segment is exactly zero length, extrapolation
fails. No epsilon tangent is synthesized.

### 2.6 Projection to a Path distance

Projection subdivides each cubic into 24 equal `t` intervals in both modes.
Setup-length mode assigns each segment a linearly proportional portion of the
curve's declared cumulative-length interval. Constant-speed mode computes the
24-segment local cumulative Euclidean length and scales it into that curve's
four-segment coarse-length interval. The projection table is only an inverse
query aid; forward constant-speed sampling still uses the ten-segment lookup
from section 2.3.

For each non-zero segment:

```text
r = clamp(dot(target-from, to-from) / lengthSquared, 0, 1)
projected = from + (to-from)*r
distance = lerp(segmentDistanceFrom, segmentDistanceTo, r)
```

Candidates are visited in curve and subdivision declaration order. A candidate
replaces the current best only when its squared spatial distance is strictly
smaller. Equal-distance ties therefore select the earliest Path segment.

An open Path additionally considers the two infinite endpoint tangent rays.
The direction uses the first anchor/outgoing-handle or final incoming-handle/
anchor segment. Only a negative projection on the start tangent or a positive
projection on the end tangent is eligible. A closed Path has no tangent-ray
projection. Non-finite candidates and exactly zero-length projected segments
are ignored.

For `positionMode == "percent"`, a projected fixed distance is divided by
`totalLength`; the sampler already guarantees `totalLength > 0`.

### 2.7 Position and spacing

The starting Path distance is:

```text
distance = position                       for positionMode fixed
distance = position * totalLength         for positionMode percent
```

For a driven bone:

```text
setupLength = max(bone.length, 0)
worldLength = setupLength * hypot(world.a, world.b)
```

Let `spacingWorldLengths` contain that value for each driven bone. Let:

```text
spacingBoneCount =
    max(boneCount - 1, 0)                 for rotateMode tangent
    boneCount                             for rotateMode chain or chainScale

proportionalWorldLength =
    sum(first spacingBoneCount spacingWorldLengths)

spacesCount =
    boneCount                             for rotateMode tangent
    boneCount + 1                         for rotateMode chain or chainScale
```

The spacing added after a bone is:

| `spacingMode` | distance increment |
| --- | --- |
| `length`, `setupLength > 0` | `max(setupLength + spacing, 0) * worldLength / setupLength` |
| `length`, `setupLength == 0` | `spacing` |
| `fixed`, `setupLength > 0` | `spacing * worldLength / setupLength` |
| `fixed`, `setupLength == 0` | `spacing` |
| `percent` | `spacing * totalLength` |
| `proportional`, total `> 0` | `raw / proportionalWorldLength * spacing * totalLength`, where `raw = worldLength` when setup length is positive and `raw = spacing` otherwise |
| `proportional`, total `== 0` | `spacing * totalLength / max(spacesCount,1)` |

The first driven bone uses the starting distance. Before every later driven
bone, add spacing for the preceding bone. A second sample for the current bone
uses `distance + currentBoneSpacing`.

### 2.8 Rotation, translation, scale, and reflection

For each driven bone in constraint `boneIds` order:

```text
sample = samplePath(distance)
next = samplePath(distance + currentBoneSpacing)
chainAngle = angle(next.point - sample.point)

desiredRotation =
    sample.tangent                         for rotateMode tangent
    chainAngle                             for rotateMode chain or chainScale

desiredRotation += signedRotationOffset
```

Reflection changes the configured rotation offset, not the sampled Path:

```text
slotDeterminant = det(targetSlotBoneWorld)
signedRotationOffset =
    -rotation                              if rotation != 0
                                             && slotDeterminant < 0
    rotation                               otherwise
```

The exact determinant-zero case is not reflected.

Mixing is:

```text
mixedX = currentX + (sampleX-currentX) * clamp(mixX,0,1)
mixedY = currentY + (sampleY-currentY) * clamp(mixY,0,1)

rotationDelta = wrapDegrees(desiredRotation-currentRotation)
mixedRotation =
    currentRotation + rotationDelta * clamp(mixRotate,0,1)
```

For `chainScale` and `bone.length != 0`:

```text
targetLength = distance(next.point, sample.point)
desiredScaleX = targetLength / abs(bone.length)
mixedScaleX =
    currentScaleX + (desiredScaleX-currentScaleX)
                  * clamp(mixRotate,0,1)
```

Otherwise world scale X is unchanged. The solved world matrix uses
`mixedX`, `mixedY`, `mixedRotation`, zero world shear X, the current world
shear Y, `mixedScaleX`, and the current world scale Y.

Sampling and mixing MUST preserve the authored `boneIds` order, but every
`current*` value above comes from the immutable world-state snapshot that
existed immediately before this Path constraint began. Updating an earlier
driven parent MUST NOT first recompute a later driven child's current matrix.
During the ordered pass, Core stores each driven bone's desired world matrix.
Once the pass finishes, Core commits stored desired worlds in skeleton parent-
before-child order. Each driven local pose is reconstructed against its
already committed final parent; non-driven bones are recomputed once from
their current local pose only when they descend from a Path-driven bone.
Unrelated branches MUST retain the result produced by earlier constraints.
This hierarchy commit MUST leave every stored driven world matrix intact for
both parent-first and child-first authored lists. It is part of the one Path
constraint solve, not a second animation sample or constraint pass.

For `rotateMode == "chain"` with configured `rotation == 0` and
`mixRotate != 0`, the next bone's starting sample is adjusted toward the
fully rotated current bone tip:

```text
fullDelta = wrapDegrees(chainAngle-currentRotation)
rotatedFirstColumn = rotate(currentMatrix.firstColumn, fullDelta)
fullTip = sample.point + bone.length * rotatedFirstColumn
nextChainPoint = lerp(next.point, fullTip, clamp(mixRotate,0,1))
```

This adjustment is used only as the following driven bone's `sample`; it does
not alter the Path sampler.

Path activation additionally requires the target slot bone to be active. Once
active, the declared driven-bone list is solved as a unit; member-skin
activation does not prune individual Path-driven bones.

## 3. Independent Physics profile: spring-v2

The independently derived scalar transition and its numerical evaluation are specified in
[Spring Dynamics Design v2](SPRING_DYNAMICS_DESIGN_V2.md). This section specifies the bone adapter.
Use binary64 for spring state and intermediate dynamics, then validate and round public matrices
and diagnostics to binary32. This profile-specific rule overrides the shared intermediate
arithmetic convention; the public Frame/RenderPacket geometry remains binary32.

### 3.1 State and activation

Each constraint stores:

- an optional previous unconstrained world origin `p` and bone X-axis vector `r`;
- displacement and velocity for world translation X/Y;
- angular displacement `theta` in radians and angular velocity;
- logarithmic X stretch `lambda` and its velocity.

There is no previous rendered-tip, output-lag or fractional-time history. `r` is
`max(bone.length, 0) * (world.a, world.b)` at this constraint's position in the
ordered solve, before this constraint modifies the matrix. Other earlier constraints
therefore participate in the rest geometry normally.

Existing skin activation, constraint declaration order, sampled property timelines,
global Physics property opt-ins and reset-key edge crossing remain unchanged.
`mix == 0` skips evaluation and freezes this constraint's history. Reset clears every
spring and removes the previous rest frame. The first evaluation after reset records
current rest geometry and publishes that pose without integrating elapsed time.
A disabled channel is cleared when evaluation runs; rotation is enabled when either
Rotate or Shear X influence is positive.

### 3.2 Rest-motion input and movement limits

For a subsequent evaluation, let `dt` be elapsed seconds, `I=clamp(inertia,0,1)`,
and `p0,r0` the previous rest geometry. Incoming pivot displacement is:

```text
raw[j] = I * (p[j] - p0[j])
cap[j] = max(limit,0) * dt * abs(root.scale[j])
pivot[j] = clamp(raw[j], -cap[j], cap[j])
translation[j].displacement -= pivot[j]                 [enabled axes only]
```

For nonzero `L=length(r)` and `L0=length(r0)`, define `u=r/L` and use:

```text
theta -= I * wrapPi(angle(r)-angle(r0))
         + cross(u,pivot)/L                             [rotation enabled]
lambda -= I * (ln(L)-ln(L0)) + dot(u,pivot)/L            [stretch enabled]
```

`wrapPi` gives `[-pi,pi)`. If either length is exactly zero, omit these undefined
angular/stretch transfers; do not divide by an arbitrary epsilon. Pivot limiting
bounds incoming motion, not accumulated spring position, velocity or applied force.
The required-limit diagnostic compares relevant raw pivot components against
`dt*abs(root.scale[j])`; it is unknown when a nonzero requirement cannot be represented.
Record the current rest frame after successful evaluation even if pivot input saturated.

### 3.3 Force, damping and time integration

Let `R=referenceScale > 0`, `m=mass > 0`, `k=max(strength,0)` and
`d=60*(1-clamp(damping,0,1))` in inverse seconds. The factor 60 is an explicit
parameter calibration, independent of the constraint's selected FPS.
Damping 1 is undamped; damping 0 gives drag rate 60. This continuous drag mapping
is a new profile rule and does not claim the retired per-step damping response.

Environment vectors retain the existing public convention (default positive gravity Y
acts downward in the mathematical Y-up pose). The world force and generalized accelerations are:

```text
Fx = R * (wind*environment.windX + gravity*environment.gravityX) * root.scaleX
Fy = -R * (wind*environment.windY + gravity*environment.gravityY) * root.scaleY
translationAcceleration = (Fx,Fy) / m
angularAcceleration = cross(r,(Fx,Fy)) / (R*R*m)
stretchAcceleration = dot(r,(Fx,Fy)) / (R*R*m)
```

For each enabled channel solve `q'' + d*q' + (k/m)*q = acceleration` using the
specified scalar transition. Generalized force uses the current unconstrained `r`
throughout this evaluation; it does not use the mixed or previously rendered output.
This is a linearization about the rest geometry, not a full rigid-body simulation.

For initialized enabled constraints with positive `dt`, compute elapsed frames with
one binary32 rounding, `f32(f64(dt)*f64(fps))`, take their ceiling `N`, and evolve N intervals of
`f64(dt)/N` seconds. Consume the whole interval, including fractions of a nominal
step. FPS bounds interval size up to that binary32 rounding; it does not quantize
output to a previous frame. In particular `f32(1/60)` at 60 FPS costs one step, and
one second at 500,000 FPS costs exactly 500,000 steps; no rounded reciprocal is used.
For fixed rest geometry and coefficients, interval subdivision gives the same ODE
solution up to numerical rounding. With changing rest poses/forces, callers still
need an explicit sampling schedule for deterministic replay.

### 3.4 Matrix output

Use the current unconstrained matrix as the starting point. Add
`translation[j].displacement * mix * influence[j]` to the origin. Apply a world
rotation by `theta*mix*rotate` to both matrix columns, then rotate only the X column
by `theta*mix*shearX`. These are output weights, not feedback gains.

For enabled X scale, let `s=exp(lambda*mix*scaleX)`. Multiply the X column by `s`.
For Y mode None use multiplier 1, Uniform use `s`, and Volume use
`exp(-lambda*mix*scaleX)`. Volume therefore preserves the determinant of the matrix
present immediately before stretch. Authored reflected axes keep their sign.
There is no special branch at a scale of 0.7 and no compression-floor polynomial.
Non-representable output is a structured numerical error, not an implicit clamp.

After validating the state, matrix and diagnostics, update the applied local pose
and propagate descendants using the existing runtime affine contract. No editor or
renderer layer re-solves or composes this geometry.

### 3.5 Host root motion

Move leaves histories in place; Teleport resets them. For Preserve Inertia and Clear
Inertia, require a finite invertible previous root, then form the full root affine delta.
Transport rest origins as points, rest axes and translation states as vectors.

For angular/stretch history, reconstruct the simulated axis
`z=exp(lambda)*rotation(theta)*r` and its differential
`zVelocity=lambdaVelocity*z + thetaVelocity*perp(z)`. Transform both as vectors.
Recover angle relative to the transformed rest axis and log length ratio. Recover
angular velocity with the tangent projection of transformed `zVelocity` divided by
transformed length, and stretch velocity with its axial projection divided by length.
Preserve angular turn count by choosing the nearest equivalent angle to the original
angle, reversing its sign when the affine determinant is negative. When either
transformed axis collapses to zero length, clear angular/stretch history.

Clear Inertia performs the same transport, then clears all four velocities.
Validate all selected histories before changing any of them. Root transport does not
advance animation time or integrate forces by itself.

### 3.6 Diagnostics, resource bounds and errors

Translation offset/speed are vector magnitudes. Angular values are reported in degrees.
Scale offset is `exp(lambda)-1`, and scale speed is `exp(lambda)*lambdaVelocity`;
these remain relative-scale diagnostic units rather than exposing internal logarithms.
The fixed-step count is N from this operation, with zero for initialization/no elapsed time.

One public operation permits at most 1,000,000 aggregate Physics intervals across all
constraints and replay evaluations. Preflight N in constant time and reserve against the
shared remaining budget before evolution. The API's separate absolute-replay and event
limits still apply. Never silently reduce steps or clamp elapsed time to fit the budget.

Finite state and public-output checks precede publication. On a numerical or resource
failure, the constraint state, local/world pose and budget remain unchanged; public
Runtime API operations additionally retain their previously published frame and time.
Public invalid elapsed time is rejected at the API boundary. Reset/inactive behavior
is not an escape hatch for accepting invalid serialized constraint parameters.

## 4. Slider constraints

### 4.1 Validated setup domain

A Slider references one target animation and optionally one source bone.
`sourceOffset`, `timeOffset`, `timeScale`, manual `time`, and `mix` are any
finite binary32 values. In particular:

- `timeScale == 0` is valid and maps every source value to `timeOffset`;
- negative `timeScale` is valid;
- `mix` may be negative or greater than one;
- only exact `mix == 0` disables the Slider.

`rangeMax` is finite and non-negative authoring/inspection metadata. Runtime
API v1 does not use it to clamp source values, mapped time, or target animation
time.

A Slider timeline has two continuous channels. Under API 1.10 pose-v2 both use the
outer track's replace/additive mode and additive setup-relative deltas. For example,
setup time 0.1, base time 0.4 and full-opacity additive time 0.8 yield 1.1.
The historical pre-1.10 policy below is retained for traceability only:

- `sliderTime` always uses replace blending, even on an additive outer track;
- `mix` uses the outer track's replace/additive blending.

Target animation layers in API 1.10 use the [versioning](VERSIONING.md)
over the current solved underlay, with unbounded finite Slider coefficients. Fixed-frame
sampling quantizes the resolved target time exactly once. Layer failures roll back the
whole API operation; the work ledger is shared across Sliders and replay frames.

Every finite non-zero resulting Slider mix remains active.

### 4.2 Source-space mapping

With `sourceBoneId == null`, the Slider is manual and begins with:

```text
sourceValue = absent
mappedTime = slider.time
```

With a source bone, that bone and the Slider constraint must both be active.
The source is sampled after all preceding declared constraints.

For `local == true`, read the current applied local pose directly:

| property | value |
| --- | --- |
| `rotate` | `pose.rotation` |
| `x` | `pose.x` |
| `y` | `pose.y` |
| `scaleX` | `pose.scaleX` |
| `scaleY` | `pose.scaleY` |
| `shearY` | `pose.shearY` |

For `local == false`, first remove the Runtime API root transform:

```text
M = inverse(rootMatrix) * sourceBoneWorldMatrix
```

The inverse exists only for a finite, exactly non-zero determinant. Failure is
a deterministic Slider no-op. Then:

| property | value |
| --- | --- |
| `rotate` | `degrees(atan2(M.b,M.a))`, adding `360` once when negative |
| `x` | `M.tx` |
| `y` | `M.ty` |
| `scaleX` | `hypot(M.a,M.b)` |
| `scaleY` | `hypot(M.c,M.d)` |
| `shearY` | `degrees(atan2(M.d,M.c))-degrees(atan2(M.b,M.a))-90` |

World `scaleX` and `scaleY` are magnitudes. World `shearY` is deliberately not
wrapped. The root transform is removed exactly once, so a root-space host
transform cannot leak into Slider source mapping.

Source mapping is:

```text
sourceValue = sampled property
mappedTime =
    timeOffset + (sourceValue-sourceOffset)*timeScale
```

A non-finite result from arithmetic overflow makes the Slider a no-op. This check happens
before any loop or clamp operation, so an infinite result cannot become a valid endpoint.
When `timeScale == 0`, use finite `timeOffset` directly without computing the unused source
difference; all source and parameter values must still be finite. Non-zero mappings retain
binary32 subtraction/multiplication/addition order.

### 4.3 Loop, clamp, and manual-time rules

Let `D` be the target animation duration.

For a bone-sourced Slider:

```text
if looping && D > 0:
    resolvedTime = euclideanRemainder(mappedTime,D)
else:
    resolvedTime = clamp(mappedTime,0,max(D,0))
```

Thus a non-looping bone source is clamped and a negative looping source wraps
with Euclidean, not signed, remainder.

For a manual Slider:

```text
if looping && D > 0:
    resolvedTime = euclideanRemainder(mappedTime,D)
else:
    resolvedTime = mappedTime
```

A non-looping manual Slider is not numerically clamped. Normal channel sampling
leaves values unchanged before their first key and holds the last applicable
key after it. This distinction is observable in Slider diagnostics.

The `D > 0` test is exact. The smallest positive binary32 duration loops.
When `D == 0`, a bone-sourced Slider clamps to zero while a manual Slider keeps
its finite manual time.

`wrapped` is true only when the resolved looping value is not exactly equal to
`mappedTime`. `clamped` is true only for a bone source when the bounded value
is not exactly equal to `mappedTime`. No epsilon is used.

Fixed-frame sampling, when requested by the Runtime API sampling options, is
applied by the target animation's channel sampler after `resolvedTime` is
computed. A diagnostic's resolved time is the pre-quantization value.

### 4.4 Applying the target animation

If `mix` is finite and not exactly zero, sample the target animation at
`resolvedTime` as one animation layer:

```text
replace:  current + (sampled-current)*mix
additive: current + (sampled-setup)*mix
```

Rotation/shear use the common shortest-angle blend. A negative or greater-than
one mix extrapolates these formulas; it is not clamped.

The target layer may affect:

- bone transforms and inheritance;
- active skin and slot attachment selection;
- slot color/alpha;
- region pose, deform, and sequence values;
- IK, Transform, Path, Physics, and Slider sampled constraint values;
- draw order.

Discrete properties use the common exact `mix >= 0.5` selection rule where
applicable. The layer does not advance a target-animation clock and does not
emit target-animation user or lifecycle events.

After the layer, skin attachments are rebuilt. If the target animation has any
bone timelines, unconstrained world transforms are rebuilt before the next
declared constraint.

### 4.5 Declaration order and recursion barrier

The sampled constraint array is visited once from first to last. Before
applying a Slider, its current sampled definition is copied. The target
animation may mutate sampled constraints, but:

- a mutation of an earlier constraint does not rerun it;
- a mutation of the currently executing Slider does not restart it;
- a mutation of a later constraint is observed when that later declaration is
  reached;
- target-animation sampling never recursively enters the constraint solver.

These rules are the Slider recursion barrier. Self-targeting and cyclic Slider
animation graphs terminate deterministically because each declared constraint
is solved at most once per outer `apply`.

## 5. Exact branch summary

The Path rows in this legacy table apply to `constraint.path` only. Geometry-v2 retains zero-span
handling and reflected offsets, but stationary curves remain resolvable and endpoint orientation
uses higher derivatives. See its independent design for the current failure and budget rules.

| family | condition | exact behavior |
| --- | --- | --- |
| Path | `totalLength == 0` | sampler unavailable |
| Path | cumulative span `== 0` | parameter ratio `0` |
| Path | endpoint tangent length `== 0` | open extrapolation unavailable |
| Path | target-slot determinant `< 0` | negate non-zero rotation offset |
| Path | any mix is the smallest positive binary32 | contributes normally |
| Physics | `mix == 0` | no solve and no time accumulation |
| Physics | channel coefficient `> 0` | channel enabled |
| Physics | mass is any positive binary32 | use that exact mass |
| Physics | world length `> 0` | permit scale-inertia division |
| Physics | movement `> limit` | saturation diagnostic; equality is not saturated |
| Physics | preflight step count exceeds the operation budget | `resourceLimit`, no state publication |
| Slider | duration `> 0` | looping uses Euclidean remainder |
| Slider | `timeScale == 0` | source maps to `timeOffset` |
| Slider | `mix == 0` | no target layer |
| Slider | any finite non-zero mix | apply, including negative and greater than one |
| Slider | root determinant `== 0` | world source unavailable |

## 6. Required conformance coverage

The v1 conformance suite SHOULD use at least these independently loadable
cases. Case IDs are stable recommendations for the shared manifest:

| case ID | required observations |
| --- | --- |
| `path-constant-speed-open-extrapolation-v1` | four-step coarse distance, ten-step local parameter lookup, exact cubic sample/tangent, raw-handle extrapolation, projection tie order, and parent-first driven-bone snapshot isolation |
| `path-setup-lengths-weighted-deform-v1` | authoritative non-uniform cumulative lengths, deterministic missing-length fallback, weighted world points, both deform spaces |
| `path-modes-reflection-order-v1` | fixed/percent position; all spacing and rotate modes; chain tip adjustment; chainScale; reflected target-slot rotation offset; following-constraint observation |
| `physics-fixed-step-environment-reset-v1` | custom environment, complete fractional interval, reset followed by independent re-evolution |
| `physics-spring-v2-ballistic-*` | analytic constant-force displacement, FPS 1/60/240 and equivalent interval partitions |
| `physics-spring-v2-undamped`, `physics-spring-v2-critical`, `physics-spring-v2-overdamped` | independent closed-form solutions for each damping regime |
| `physics-spring-v2-volume-*` | exponential axial compression, reciprocal transverse scale, determinant preservation including strong compression |
| `physics-spring-v2-host-*` | active reflected affine transport, preserved/cleared/reset velocity and subsequent force integration |
| `physics-spring-v2-reset` | reset then same elapsed interval reproduces initial motion |
| `physics-resource-limit-atomic-v1` | aggregate exactly-at-limit success, one-over-limit `resourceLimit`, multiple constraints, apply/advance/absolute-sample rollback |
| `slider-manual-source-mapping-v1` | manual time, all local/world properties, root removal, singular root, zero/negative timeScale, metadata-only rangeMax |
| `slider-loop-clamp-additive-order-v1` | negative Euclidean loop, bone-source clamp, manual non-clamp, replace/additive unbounded mix, discrete threshold, earlier/later constraint observation |
| `slider-recursion-barrier-v1` | self and cyclic target animations, current/earlier constraint not rerun, later constraint mutation applied once |

Every case containing geometry MUST compare complete matrices, final vertices,
indices, and final light/dark tint where applicable. Physics cases MUST compare
repeated runs and operation partitioning only where this specification defines
the streams as equivalent; stateful Physics is evaluated at each published
sample and is not generally invariant to replacing many sampled poses with one
large pose change.
