# Cane Transform channels v2

## Requirements and independent model

Retain local/world source and destination choices, absolute/relative modes, six independent
finite mix values (including extrapolation), signed scales, authored local shearX, offsets,
ordered many-to-many property mapping, optional output clamping, Match and descendant updates.
Inputs remain readable. Neither a small positive axis nor exactly zero mix is reclassified by
an epsilon activation threshold. Invalid final values fail explicitly and cannot partially
publish members of a constraint.

## World channels and reflection

Write a world matrix as columns `X=(a,b)`, `Y=(c,d)` and origin `p=(tx,ty)`.
Use channels `q=[theta,tx,ty,sx,sy,phi]`, angles in degrees, with reconstruction

```text
X = sx * (cos(theta), sin(theta))
Y = sy * (-sin(theta+phi), cos(theta+phi))
```

For nonzero X, let `u=X/|X|`, `v=(-u.y,u.x)`. Then

```text
sx = |X|
theta = angle(u), wrapped to [-180,180)
sign = -1 when det(X,Y)<0, otherwise +1
sy = sign*|Y|
phi = atan2(-sign*dot(u,Y), sign*dot(v,Y))
```

The dot products are coordinates in an orthonormal basis. Their squared sum is `|Y|²`,
which proves reconstruction of Y; the determinant sign is carried by sy. Reflection is not
encoded as an otherwise hidden half-turn in shear. For zero Y choose phi=0. For zero X but
nonzero Y choose theta=angle(Y)-90°, sx=0, sy=|Y|, phi=0. For both zero choose theta=phi=0.
These last choices fix the free orientation deterministically; an absolute scale write can
restore the collapsed axis. A relative scale multiplier leaves a zero axis zero.

Before writes, decomposition chooses sx>=0. Writes may assign a negative sx or sy; these are
retained until matrix reconstruction. Within one constraint, routing acts on this one channel
tuple rather than repeatedly decomposing after every write. At the next world constraint a new
tuple is derived from the then-authoritative matrix. This is an explicit choice of coordinates,
not a claim that a matrix uniquely reveals the original signed local scales.

Local channels are `[rotation,x,y,scaleX,scaleY,shearY]` from the sampled/applied local pose.
Local shearX is not a routed channel and remains unchanged. The adapter uses the authoritative
runtime FK and inheritance mode to turn a resulting local pose into its world matrix.
The world-channel representation does not replace that FK or any bone inheritance algorithm.

## Source signals and routing

Capture the source pose once per constraint. Apply the six setup offsets before routing:

- local source: componentwise addition to its six channels;
- world source: componentwise addition for theta/sx/sy/phi, while the position signal is
  `sourceMatrix * (offsetX,offsetY,1)`;
- positive rotation and shear offsets mean counterclockwise in the declared coordinate space.
  A reflection does not negate the offset; signed sy carries the reflection.

Local authored angle values can contain whole turns. World theta comes from the matrix and is
in `[-180,180)`. Offsets may introduce whole turns in either space; no unrelated normalization
of a scalar source occurs when that source is routed to a position or scale channel.

An ordered route records source/destination channels, source origin s0, destination origin d0,
gain g, and optional destination limit d1. Its scalar signal is

```text
z = d0 + (source[channel]-s0)*g
when clamped: z = projection of z onto [min(d0,d1),max(d0,d1)]
```

This is the equation of the line sending s0 to d0, with interval saturation. Flatten source
entries and their destination lists in declaration order. Multiple writes to one destination
observe its previous written value. All sources stay captured, so destination writes cannot
feed back into this constraint's source. Canonical mode supplies six identity routes to the
same evaluator; there is no second set of canonical mixing formulas.

For destination mix m (any finite number):

| Channel | Absolute | Relative |
| --- | --- | --- |
| x, y, sx, sy | `(1-m)*q + m*z` | x/y: `q+m*z`; sx/sy: `q*((1-m)+m*z)` |
| theta, phi | `wrap(q+m*wrap(z-q))` | `wrap(q+m*z)` |

`wrap` reduces modulo 360 before adjusting by 180, and chooses -180 for exact half turns.
Absolute angles use shortest-turn interpolation consistently for local/world and canonical/
mapped targets. Relative angles retain whole turns in z until after mixing (720° at mix 1/4
contributes 180°). These choices can change old mapped-local and relative-world results and
therefore require the new explicit profile. m=0 leaves the destination bit-for-bit untouched.
An equal absolute signal or neutral relative signal also preserves the original channel bits,
including signed zero and authored whole turns, rather than performing canceling arithmetic.
An adapter must preserve the original world matrix when the complete channel tuple is unchanged,
rather than decomposing/reconstructing a zero-effect operation and creating rounding drift.

## Match as an inverse problem

Match applies to canonical one-to-one, unclamped routes, including different source and target
spaces. For absolute mode its desired signals are the controlled bone's channels. For relative
mode desired signals are `[0,0,0,1,1,0]`, the neutral values of the equations above.
Subtract source channels to obtain the rotation/scale/shear offsets; absolute angle differences
may be reduced to a shortest representative, whereas relative neutral offsets cancel the raw
source angle, including whole turns. Local position offsets are simple differences.

For a world source matrix `[A,p]`, positional Match solves `A*o=desiredPosition-p`.
For nonsingular A, use the 2x2 inverse in binary64. For rank-one A, a consistent right side b has
the minimum-norm solution `o=A^T*b/||A||F²`. To see this, write `A=u*v^T` and b=t*u;
the solution is `t*v/|v|²`, perpendicular to A's nullspace. Verify the resulting point with a
roundoff-scaled residual before accepting. At rank zero, only b=0 is solvable, choosing o=0.
An inconsistent singular request or unrepresentable offset reports failure; it is not silently
projected to a different target. General routing inversion remains outside the existing Match
contract because many-to-one, clamped or cross-property mappings can be underdetermined.

## Numerical and transactional boundaries

Promote finite binary32 inputs to binary64 before norms, products, determinants, differences and
routing. Products of two binary32 components fit binary64, including extreme/subnormal matrices.
Reject nonfinite configuration; reduce angular results before binary32 conversion. Position
and individual matrix components use ordinary finite binary32 rounding. A nonzero scale/axis
that would be lost entirely on conversion is unrepresentable; exact intended zeros are allowed.
No arbitrary world-axis epsilon is used. Candidate methods take values and return new values;
they do not mutate source poses on error.

Production integration must stage the affected local poses/world states and validate all affected
descendants before committing a constraint. Preserve already-solved unrelated branches. Propagate
numerical/work-limit errors through Runtime API with the constraint ID. Missing inactive sources
remain scheduling decisions, distinct from failed mathematics.

World writes and subsequent local consumers require special care under a singular parent or a
nonstandard inheritance mode. The adapter must specify whether a local result is exactly
representable; it must not publish a world update while silently reusing an inconsistent applied
local cache. The following policy was established before writing the applied-pose adapter;
its failure/continuation behavior is now covered by both runtime and public API tests.

### Applied local pose after a world write

The new profile requires a finite applied local pose that reproduces the requested world columns
under the authoritative FK, to a binary32 rounding bound. A world request outside that FK's
reachable set returns a structured failure before publishing this constraint. This differs from
silently keeping an inconsistent local cache; it does not reject all singular parents.

First solve the origin equation `P*t = requestedOrigin-parentOrigin`. In a singular but
consistent system, choose the solution nearest the previous local origin: solve for a correction
to that origin using the minimum-norm rank-one solution above. At full rank, solve directly:
subtracting and re-adding a large reference can erase a tiny but representable target. Use the same nearest-solution
principle for unconstrained axis vectors when their enclosing basis is rank deficient.

With API 1.8 polar inheritance, every mode has a parent-only linear operator B. Obtain B
from the shared polar kernel; do not probe a unit child and narrow the basis early, because a
finite final child can use a stretch coefficient larger than binary32 MAX. Solve each desired
column through B, preserving the reference nullspace when the operator loses rank. For
IgnoreScale/IgnoreScaleAndReflection the inherited Q/R remains orthogonal at every rank and is inverted
by transpose. For left stretch use `S^+ = Q P^+`, determining rank from the original widened P.
See the complete [inverse derivation](INHERITANCE_POLAR_DESIGN_V2.md#inverse-and-representability-contract).

Finally evaluate the candidate with the actual FK and compare each column to the requested
column using `64*epsilon_f32*max(column norms)`; translation uses the same relative bound with
the magnitude of its parent/local product terms to allow cancellation. The minimum absolute
bound is one binary32 subnormal. Failure to reproduce the request is explicit, including
overflow, complete underflow of a requested nonzero axis, and numerical failure of shared FK.
Preserve the exact original matrix and local pose for a zero-effect
channel write. A successful changed write publishes the requested world matrix together with
its checked applied local pose; subsequent local constraints use this pose.

For a rank-one FK inverse, the binary32 request or recovered ray may fall outside the exact
image by an input rounding error. A minimum-norm projection is accepted only within the same
binary32 bound, and the final whole-matrix FK reconstruction must still pass. This allowance is
specific to reconstructing FK output; it does not relax the binary64 consistency check used by
the independent Match point solver.

### Ordered sparse transaction and work budget

Prepare source signals and ordered routes once, before any controlled member changes. Stage
changed local/world entries in separate sparse maps; reads consult these maps before the original
state. Visit members in declaration order, then each member's descendants in parent-before-child
order. A later ancestor update can therefore rebuild a previously controlled descendant from its
already-staged local pose. Unrelated branches keep their existing solved world overlays.
If the source is a descendant, its propagated world pose may change, while this constraint's
captured source signals stay fixed. A source explicitly listed as a member is skipped.

The profile uses 1,000,000 work units per constraint: one per prepared route, one plus
twice the route count per active controlled member (actual and full-mix diagnostic passes), and
one per propagated descendant. This bounds repeated descendant/routing work; it is not an exact
CPU instruction count. Check before each unit group. Exceeding the budget, a failed member,
or a failed descendant discards both sparse maps; no member is published early.
`MAX_TRANSFORM_WORK_UNITS_V1` exposes that limit at the Runtime API. Budget exhaustion is
`resourceLimit`; nonfinite channel data is `nonFinite`; failed representability under the
active FK/binary32 rules is `invalidState`. Each error identifies the constraint. The C adapter
forwards these results and the same limit without implementing another solver or counter.

A full-mix diagnostic candidate does not need applied-local recovery. If its finite world
candidate cannot be evaluated, the optional residual remains unavailable; a valid actual
partial write still succeeds. This does not suppress any error in the requested actual solve.
Route configuration validation must not evaluate an invented destination with the actual mix:
an exact fixed point can remain unchanged even when extrapolation from some other value overflows.

## Compatibility and verification

The profile requires Runtime API minor 7, `constraint.transform.channels-v2`, and capability
bit 45. Container and playback naming requirements are defined in [versioning](VERSIONING.md).
The conformance matrix covers affine/reflected matrices, signed and zero scales, shortest and
multi-turn angles, negative/extrapolated/zero mixes, route order, rank-one reachable targets,
parent/child order, downstream consumers, descendant overflow and failure atomicity.
