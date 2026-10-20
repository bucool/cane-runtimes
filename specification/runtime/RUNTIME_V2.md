# Runtime contract under development

## Target and compatibility

The capability target is the official Spine Core and PixiJS runtime **4.3.13**.
The implementation is independent. Spine is a behavioral and performance comparison,
not a production dependency. The target includes 4.3 Slider constraints, mapped
Transform properties and draw-order folders, as well as ordinary skeletal playback.

V1 API shape, serialized runtime formats, numeric error ordering and algorithms are
retired. Historical fixtures keep their original answers and describe the old contract.
New tests use analytic geometry, official-runtime observations and cross-language
comparisons. No compatibility facade or second production evaluator is required.
User authoring projects and source assets must not be removed or overwritten.

The optional v1 C ABI tool is retired; it is not a required distribution layer.
Its source and the previous conformance runner/expected answers are archived. The
new leaf conformance tool samples actual v2 exports and compares Rust/TS snapshots,
including Physics stepping and events. Production crates do not depend on it.

## Representation

Loading resolves names to integer indices, establishes parent order, binds timelines
to scalar pose offsets and constructs the bone/constraint update order. Shared data
contains setup values, timelines, geometry, skin attachment tables and this schedule.
Instances own current scalar pose values, world matrices, track cursors, deformation,
physics state and reusable rendering buffers. Shared data contains no instance state.

Bone local transforms use x, y, rotation, scaleX, scaleY, shearX, shearY and inheritance.
Angles are degrees. A matrix `[a,b,c,d,x,y]` maps a point to
`[a*px+c*py+x, b*px+d*py+y]`. Runtime math uses double precision in both languages;
GPU vertex buffers use float32. Skinning transforms each influence's bone-local bind
point plus deformation into world space before weighting. No skin binding is inferred
from a previously deformed frame.

Timeline values are bound to final scalar channels at load time. Native Cane authoring
keys are already absolute; the compiler does not add setup offsets a second time.
Foreign formats must resolve their offsets and multiplicative setup scale before this
boundary. The evaluator sees absolute keys plus channel-specific mixing rules.

Linear and stepped segments are stored directly. Each Bezier retains one parametric
cubic per authored interval, without expanding it into approximation segments. Curved
intervals store `[value, signedInverseDuration, Cx, Bx, Ax, Cy, By, Ay]`:
`X(u) = ((Ax*u+Bx)*u+Cx)*u` and `Y(u) = value+((Ay*u+By)*u+Cy)*u`.
The duration sign marks whether both normalized X handles lie in [0,1]. Ordinary
curves use bounded Halley iterations; flat tangents use a bracketed Newton/bisection fallback.
Backtracking X handles split the search at derivative extrema and select the first
intersection. The parameter correction tolerance is 1e-12; the fast path tests the
residual against the slope so tiny steps near flat tangents cannot terminate early.
Authored endpoints remain exact.
An inverse duration of zero means linear/stepped, with slope in `Cy`. Timelines with
only linear/stepped intervals use two coefficients per interval: value and slope.
At a duplicate key time the last key wins; the preceding interval still ends at the
first key at that time. Normalized Y handles scale by the endpoint delta; value-offset
handles stay relative to their respective endpoints, including equal-value excursions.

## Execution and ownership

### Export representation

Runtime v2 exports contain an indexed model and typed numeric buffers. The authoring
compiler prepares every scalar and deformation curve before export.
`model.curves` references one shared f64 buffer. Each timeline is the tuple
`[channel, kind, offset, count, stride]`: kind 0–4 means number, angle, scale, discrete,
attachment; offset is a scalar index into the pool; count time values are followed by
count × stride coefficients. Stride is two or eight. Times are strictly increasing.
An empty timeline has count zero. Deformation frame-owner indices address these same
compiled intervals. Curve coefficients retain full f64 precision.

JSON retains named model fields. Each buffer is
`{"type":"f64","encoding":"packed","length":123,"data":"..."}`: `type` is the
decoded numeric type, `length` is the scalar count, and `data` is standard padded
Base64 of the same little-endian numeric block used in binary. The encoding names
and lossless numeric rules below apply to both representations. JSON buffer blocks
start at byte zero; their decoded byte length replaces a binary view's byte count.
TypeScript programmatic documents may also supply plain or typed numeric arrays.

The JSON exporter puts `model.textures` first, followed by `model.name`. The Pixi
loader can read this prefix from the HTTP response stream and request images while
the rest of the JSON downloads. Other field orders remain readable through the full
JSON path. The asset promise resolves only after both the complete model and textures
are ready; streaming changes resource scheduling, not runtime evaluation.

Binary uses fixed records, a shared string dictionary, and aligned numeric buffers.
Both loaders expand lossless curve predictions into the same prepared f64 pool once,
before returning the asset. They do not sort authoring keys, resample animation, or
defer work to the first frame. Dense buffers still use typed views where possible.
Programmatic Timeline construction from source keys remains available.

The current development binary is CANE2IDX. Earlier development runtime exports,
including array-based JSON, need regeneration; native projects, textures and the
JSON model remain unchanged.
Validation belongs at this load boundary, not in frame loops.

#### Binary envelope and records

The 20-byte header is `CANE2IDX`, followed by three little-endian u32 byte counts:
UTF-8 JSON string dictionary, fixed metadata, and numeric body. The dictionary is a
single array of strings. Metadata follows immediately; the body starts at the next
8-byte boundary. Header sizes describe the entire file, without trailing bytes.

[The binary layout](RUNTIME_V2_BINARY_LAYOUT.json) specifies every record's ordered
fields, fixed tuple, enum and tagged variant. Metadata is Model followed by a list of
View records. Unsigned indices and counts use LEB128, limited to 32 bits. Lists start
with a count; fixed tuples and records have no field tags or field names. Booleans
are one byte, 0 or 1. An optional value starts with this same presence flag. Strings
are dictionary indices. All enum and variant indices follow the schema's order.

Numeric metadata uses tags 3 (f32), 4 (f64), 8 (positive integer magnitude), 9 (negative
integer magnitude), or 16–255 (tag minus 16). Float payloads are little-endian; integer
magnitudes are u32 LEB128. Metadata numbers must be finite. Signed integer fields
must be exact safe integers; event integer payloads additionally fit i32. Signed zero
uses a float payload. Fixed records do not accept arbitrary object fields or recursive
user-defined shapes; strings such as `__proto__` remain ordinary data.

View fields are type, encoding, offset, length and bytes, in that order. Type indices
0–2 mean f64, f32 and u32. Encoding indices 0–6 mean f64, f32, u32, u8, u16, packed,
and curves. Offsets address the numeric body; lengths count logical values. Bytes is
zero for dense encodings and the block byte length for packed or curves encodings.
Dense types match their encoding, except exact f64 values may use f32 storage and u32
values may use u8/u16 storage. Identical encoded blocks may share a body range.

Packed f64 buffers start with ceil(length/4) tag bytes, four two-bit tags per byte,
least significant first: 0 is +0, 1 is +1, 2 reads an f32, 3 reads an f64. Payload
floats follow the entire tag array in value order. Narrowing must preserve every bit.
Numeric buffers preserve negative zero, infinities and NaN payloads. Decoders never
mutate the input bytes.

#### Prepared curve encoding

The curves encoding is available only for f64 buffers and has an 8-byte-aligned body
offset. It starts with u32 segmentCount and u32 timeInfo. timeInfo >> 1 is the shared
time count; bit 0 selects f64 time storage when set, f32 otherwise. The time dictionary
follows the header. Narrowed times must round-trip exactly.

A segment plan follows the times: one u32 LEB128 code per segment. Code >> 2 is a row
count for low bits 0 (stride 2) or 1 (stride 8). Low bits 2 instead reference an earlier
segment by its index; low bits 3 are invalid. Literal segments occupy count times,
then count × stride coefficients in the output pool. A reference occupies the exact
same number of output values as its source and copies that already-decoded range.
The plan must account for the descriptor's complete logical length.

After the plan come row flags for every literal segment, followed by their payloads
in segment order. A reference has no flags or payload. For each literal segment, read
all (time index, value) pairs first, then the coefficient payload for each row. Time
indices use u8 for at most 256 dictionary entries, u16 for at most 65536, otherwise u32.
All fixed-width payloads are little-endian.

Flag bits 3–4 encode the row value: 0 reads f32, 1 means +0, 2 means +1, 3 reads f64.
Bit 7 is reserved and must be zero. The low three bits select the coefficient mode:

- 0: zero coefficients after the value.
- 1: linear slope (nextValue - value) / (nextTime - time).
- 2: a literal f64 slope.
- 3: four f32 normalized Bezier controls, reconstructed as below.
- 4: four f32 Bezier controls with Y offsets relative to each endpoint.
- 5: seven literal f64 coefficients, valid only for stride 8.

Modes 1, 3 and 4 require a following row. Slopes occupy coefficient 1 for stride 2,
or coefficient 5 for stride 8; other coefficients remain +0. Modes 3 and 4 require
stride 8. Their payload stores x1 then x2 as ZigZag u32 LEB128 differences between
the f32 bit pattern and 0x3e800000 / 0x3f400000, with wrapping 32-bit arithmetic.
Next come f32 u and v, omitted as +0 when flag bits 5 and 6 are set. These bits are
consulted only for modes 3 and 4. The resulting controls are x1, u, x2, v.

Let dt = nextTime - time and dv = nextValue - value. Mode 3 uses y1 = u*dv and y2 = v*dv;
mode 4 uses y1 = u and y2 = dv+v. Reconstruct coefficients, in this arithmetic order:
`[value, sign/dt, 3*x1, 3*x2-6*x1, 1+3*x1-3*x2, 3*y1, 3*y2-6*y1, dv+3*y1-3*y2]`.
Sign is +1 when both X controls are in [0,1], otherwise -1. The exporter uses a
prediction only after checking equality of all original and reconstructed f64 bits.
Non-finite arithmetic and other non-reconstructible rows retain literal storage.
This is a storage transform; the frame evaluator's tables and arithmetic do not change.

Texture records include storage color space (sRGB/linear), alpha association
(straight/premultiplied), min/mag filtering, optional mip filtering and U/V wrapping.
Premultiplied RGB is associated in the declared storage space. Converting that space
unpremultiplies, applies the transfer function, then premultiplies again. This replaces
the historical v1 atlas texel encoding; its pixels must not be relabeled as v2 data.
Pixi loads these settings before texture upload. A shared resource URL has one set
of storage/sampler settings in the Pixi Assets cache.

1. Advance tracks and queue events.
2. Apply animation channels directly to instance pose storage, resolving transitions
   and track layering without constructing maps or copying entire endpoint poses.
3. Run the prepared dependency order for bones, constraints and sliders. Recompute a
   dependency only if a preceding operation invalidated it.
4. Produce final attachment geometry, clipping, colors and draw order once.
5. Let an engine adapter upload the resulting buffers and submit draws.

Normal rendering consumes borrowed buffers valid until the next evaluation. A caller
that needs an owned snapshot explicitly copies the output. There is no default deep
freeze, whole-frame clone or transactional staging of every mathematical operation.
Host controls operate on indexed pose channels between sampling and world evaluation.
Queries read evaluated state; they do not run another constraint solve.

TypeScript defers recovery of local transforms after world-space constraints until a
later operation actually reads those locals. The static schedule places recovery before
parent changes and local consumers, so it never decomposes against a different parent
pose. Reading `skeleton.applied` resolves any remaining locals without changing world
matrices. Rust currently recovers them eagerly and serves as an independent comparison.
Path evaluation retains tangent vectors internally; query methods convert them to angles
only when requested. Empty attachment buffers are shared; mutable deformation and vertex
storage belongs to each instance and is allocated when needed.

Validation of serialized lengths, references and hierarchy is a load/compiler concern.
Hot loops assume compiled data. Degenerate transforms and empty geometry have defined
mathematical behavior and focused tests, rather than generic exception scaffolding.

### IK geometry contract

At full weight, one-bone IK aims the bone's positive local X axis at the world target,
including rotation/scale inheritance modes, shear and reflected scales. At partial
weight it interpolates the local rotation along the shortest angular difference.
Stretch/compress adjusts local X scale; Y scale can stay fixed, preserve aspect ratio,
or preserve area. A collapsed noninvertible basis leaves the bone unchanged.

Two-bone bend direction is measured in the parent's inherited coordinate system and
does not invert just because the upper bone's local scale is negative. The nonuniform
case solves an endpoint ellipse analytically; when two intersections are possible it
chooses the one with the smaller absolute endpoint X coordinate in the upper bone's
unrotated basis. Constraint effects do not overwrite the host's unconstrained pose.

Native Cane chains with more than two bones use a bounded world-error descent over
local rotations. A finite-difference endpoint derivative handles all inheritance
modes, with a short line search that only accepts an improvement. Straight chains
seed their bend only when still unsolved. Partial mix blends solved local rotation
and scale against the original applied pose. Scratch is allocated once for the
longest chain; the one/two-bone analytic paths do not allocate this storage. Old
authoring iteration/threshold fields do not select the retired solver.

Observed difference from Spine 4.3.13: its one-bone solver can miss the target direction
when nonuniform parents, shear and inheritance modes are combined. The new contract
uses the exact aiming invariant for those cases rather than preserving that deviation.
Comparisons retain both outputs and report the difference separately from matched cases.

### Transform and Slider contracts

Transform source and target spaces are independent. World properties exclude instance
scale; world translation retains instance translation. Source properties map through
source offset, multiplier, target offset and optional clamp. Scale addition is
multiplicative. World edits synchronize constrained local values for later local
constraints and IK. Transform target sets compile parent-first. The unconstrained host
pose is not overwritten.

Sliders apply animation at a scalar time and mix, optionally driven by a bone property.
Their channel dependencies join the same ordered schedule and may change subsequent
constraint parameters. Sliders do not emit keyed events. Looped time uses positive
modulo, including manual negative time; this differs from Spine 4.3.13's manual
negative-loop behavior. Non-loop manual time before the first key leaves it unchanged,
while bone-driven time clamps at zero. Zero-duration animation never divides by time.

Sliders also apply attachment selection, deformation, sequences and draw-order groups.
Slots written by Sliders retain separate input and applied attachment state; other
slots share the same state. World evaluation copies only the constrained inputs before
running Sliders, so additive deformation and repeated pose-only evaluation do not
accumulate. Subsequent Path constraints read the applied deformation. Host deformation
edits use the input pose; final slot geometry and draw order remain evaluated output.

### Host queries and skin selection

Attachment input selection can be resolved before a host writes deformation; this
does not run world evaluation. Path samples and Slider observations read the last
evaluated caches. Transform matching and inverse world-to-local authoring queries
reuse kernel math; editor code does not implement alternate solvers.

Native skin keys are discrete indexed animation channels. The last applicable key
on the highest contributing track selects a shared skin; explicit null selects
default bindings. Before a key, or after clearing the tracks, the instance restores
host-selected skins. Outgoing attachment thresholds and track alpha thresholds
govern skin keys, and activation/schedules rebuild only when the selected key changes.
The old holdPrevious authoring switch is retired; 4.3-style interrupted mixing
retains its automatically computed HOLD behavior.

### Attachment output

Slot attachment keys resolve through instance skin bindings, with the default skin as
fallback and later selected skins overriding earlier ones. Linked meshes share geometry;
that identity preserves local deformation when switching attachments. A different
geometry or a hidden slot resets it. Deformation buffers are allocated on demand and
are separate from scalar pose storage.

Final vertices and UVs are produced by the kernel. Rust draw views borrow active slices;
TypeScript draws expose active vertex/index counts into reusable capacity buffers. A
renderer must respect those counts and must not sample, re-skin, re-clip or compose tint.
Normal clipping keeps the interior of a simple polygon. Forced-convex and inverse modes
use its convex hull; inverse retains the exterior. The designated end slot is included
in clipping even when it is hidden. A collapsed normal clip has no visible interior;
a collapsed inverse clip leaves the mesh visible. Atlas trim Y offsets are measured
from the bottom of the original, unrotated image.


### Path geometry

Path attachments store cubic control points as 3n+1 points for open paths, or 3n
points for closed paths. Constraints resolve the slot's current attachment, including
skin selection and deformation. Open distances outside the path extend along the
endpoint tangent; closed distances wrap positively. Constant-speed paths use a reused
16-segment arc table per world-space cubic. Nonconstant paths use authored cumulative
curve lengths. Tangents use the exact cubic derivative, with lower derivative orders
used at collapsed handles. These numerical choices intentionally differ from Spine's
coarser length estimate and near-endpoint tangent approximation.

Proportional spacing distributes the specified fraction of the path among consumed
intervals, weighted by world bone lengths. Tangent mode consumes n-1 intervals; chain
modes consume n. All-zero lengths distribute intervals evenly. Chain mode mixes each
next origin toward the fully rotated tip; ChainScale scales the X axis to the next
sampled point. Constrained world matrices recover local values before descendants.


### Physics state

Each instance owns four damped springs: world X/Y offsets, rotation in radians and
relative X scale. Translation is applied before angular/scale inertia, so simultaneous
axes do not count the same movement twice. Rotation turns both matrix columns; shear X
turns only the X column. Scale Y can stay unchanged, follow X, or preserve area.

Physics uses explicit delta seconds, a fixed per-constraint step and interpolation from
the last simulation step. Spring acceleration is `(force - strength * offset) * massInverse`;
velocity retention per step is `damping^(60 * step)`. Wind and gravity are host world
vectors (defaults right and down in Y-up coordinates); referenceScale normalizes units.
Per-constraint inertia, strength, damping, inverse mass, wind, gravity and mix are normal
animatable channels. A zero mix hides output while simulation continues.

None leaves simulation state untouched and emits the unconstrained pose. Pose applies
existing offsets without advancing state. Reset clears motion and establishes the current
pose as the reference. Update advances the explicit interval; a negative interval resets.
Host translate/rotate operations move the stored motion reference. Repeated pose-only
refreshes cannot accumulate simulation time. These are the new semantics, not a promise
of reproducing Spine's absolute-clock/substep interpolation rounding or legacy Cane physics.


### Animation state and attachment evaluation

AnimationState.apply takes a Skeleton. It applies scalar channels, selects sequence
frames, then mixes local deformation into reused per-slot buffers. Call world evaluation
after host pose edits and geometry evaluation after constraints. applyPose/apply_pose is
the scalar-only operation for hosts that do not use attachment state; it never pretends
to update mesh deformation or final output.

An animation owns immutable single-entry channel bindings shared by all instances.
Interrupted transitions build only their combined channel plan. Track layering and
rotation direction history belong to each instance; history storage is allocated
only when a partial rotation mix needs it. A
transition samples into one scalar accumulator, with held-channel contributions through
interrupted mixes. Incoming scale chooses the target sign; outgoing scale follows the
base sign. Additive rotation retains authored full turns. Hosts can reset remembered
rotation directions or request shortest rotation. Attachment alpha thresholds are inclusive;
delayed incoming entries leave the preceding entry's attachments and events active.

Deform keys are local offsets for both unweighted vertices and weighted influences.
One compiled curve cursor supplies the interpolation factor for the entire vertex vector,
including overshooting Beziers. Linked meshes share Geometry identity. Unrelated geometry
resets deformation. Clearing a track resets its formerly owned deformation once; subsequent
unkeyed host edits remain untouched. Setup-pose deformation is zero.

Value-space Bezier handles use a shared interpolation curve plus a shared scalar
excursion, whose direction is transformed into each influence's coordinate space
at compilation. This preserves motion between equal endpoints without storing a
separate curve for every vertex. Explicit bind points still require the bind linear
inverse when authored deformation is expressed in slot-space vertex positions.

Point/segment queries read evaluated geometry, including clipping, without updating
animation or constraints. A segment starting inside an attachment returns time zero
and a zero normal; otherwise the earliest intersection includes touching endpoints
and collinear overlap. Entry normals oppose the segment direction.

Skins may supply bindings, active bones and active constraints. Selecting a skin also
activates ancestors of its listed bones. Constraint source activation gates execution;
slots on inactive bones do not draw. A newly enabled Physics constraint starts a fresh
motion reference. Bone/constraint schedules are filtered when skin selection changes,
not rebuilt each frame. Host-created attachments are registered per instance; adding a
weighted path also binds its new bone dependencies. Shared skeleton assets are unchanged.

World matrices for inactive bones are not refreshed. Active-bone masks determine whether
evaluated world data is meaningful; authoring selection must not interpret a stale
inactive matrix as the setup pose.

Sequences share frame resources and keep the selected index per instance/slot. Frames
can replace texture and UVs and optionally region geometry for different trim rectangles.
Hold, once, loop, ping-pong and their reverse forms are supported, including a single-frame
sequence. A negative instance index uses the setup frame. Sequence keys are discrete,
independent of numeric blend alpha; outgoing sequences return to setup/lower-track selection.
Changing to an unrelated sequence or hiding its attachment resets selection.

## Required capabilities

- Bone hierarchy, all inheritance modes, reflection, shear and host transforms.
- Scalar/vector timelines, per-channel Bezier curves, discrete keys, draw order and
  draw-order folders, all constraint parameters and attachment sequences.
- Multiple tracks, replace/additive blending, per-property transitions and hold,
  queued/empty animations, delays, ranges, reverse, looping, seek and time scaling.
- Keyed events and track lifecycle events with ordered delivery and mixing thresholds.
- Skins, skin-dependent bones/constraints, attachment replacement, linked meshes,
  region, mesh, point, bounding-box, clipping and path attachments.
- Weighted/unweighted deformation, atlas trim/rotation, sequence modes and shared data.
- One/two-bone IK, bend direction, softness, stretch/compress and scale-Y modes.
- Transform source/target spaces, additive/absolute application, property mapping/clamp.
- Path positioning/spacing/rotation modes, closed paths, weighted/deformed paths.
- Stateful Physics, all driven axes and parameters, reset/pose/update modes, host motion.
- Sliders with animation, additive application, bone-driven time and dependency ordering.
- Convex/concave clipping, forced convex hull and inverse clipping; light/dark tint,
  alpha, normal/additive/multiply/screen blending.
- Runtime control and lookup, coordinate conversion, bounds, hit queries, debug geometry,
  attachment/skin creation, resource replacement and independent instances.
- JSON/binary loading and final-output adapters, including editor setup/animation preview.

## Acceptance

Each capability needs independent Rust and TypeScript coverage and, where relevant,
an editor/export/render integration case. Tests include ordinary, degenerate and
combined cases; Spineboy is one sample, not the acceptance suite. Numeric tolerances
must be stated before recording outputs and must not hide a visual or semantic change.

Browser comparison uses the same PixiJS version, backend, visible resolution, textures,
equivalent bones/keys/constraints/vertices, warm-up and repeated alternating runs.
Report full update plus rendering for ordinary, layered, many-instance and constrained
scenes, distributions and A/A noise. Cold loading, peak memory and native Rust cost
are measured separately. An isolated kernel speedup does not establish overall success.

Minified and gzip output include the same functionality and dependency boundary on
both sides. Both must be no larger than Spine and pre-rewrite Cane; record compressor,
versions and hashes. New loading or memory regressions cannot buy a frame-time win.
