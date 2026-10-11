# Cane IK affine geometry v2

## Requirements

Keep one-bone aiming, two-bone bend selection, compression/stretch, transverse scaling,
softness, partial mix, and multi-bone pose solving. Preserve authored translations, both shears,
signed scales, chain connectivity, and inheritance choices. Solving must not clear child Y or
parent shear to make a simpler solver applicable.

## Two rotations with full local affine geometry

For a directly connected pair with ordinary affine inheritance, write the first bone's local
linear part as `R(alpha) H`. `H` contains its two signed scales and its two authored shear
angles. Let `p` be its translation and `v` the child's authored translation, including Y.
Let the last bone's tip vector have signed length `ell = child.length * child.scaleX` and
angle `beta = child.rotation + child.shearX`. Its Y-axis/shearY does not move its own X-axis tip,
but remains part of the published pose.

With outer affine frame `G`, the tip is:

```text
tip = G * (p + R(alpha) * [H*v + H*(ell*cos(beta), ell*sin(beta))])
```

For an invertible `G`, transform the target once to `T = inverse(G)*target - p`.
Define `h=H*v`, `u=ell*H.columnX`, `z=ell*H.columnY`. The remaining equation is:

```text
R(alpha) * e(beta) = T
e(beta) = h + u*cos(beta) + z*sin(beta)
```

Rotation preserves norm, so first solve `|e(beta)| = |T|`, then take
`alpha = angle(T) - angle(e(beta))`. This includes nonuniform scale, reflection, both parent
shears, and nonzero child Y without deleting data. It does not claim that changing an
inheritance mode has ordinary affine semantics; those cases need the separate adapter below.

### Bounded polynomial solution

For `t=tan(beta/2)`:

```text
cos(beta) = (1-t*t)/(1+t*t)
sin(beta) = 2*t/(1+t*t)
A = h+u, B = 2*z, C = h-u
|A+B*t+C*t*t|^2 - r*r*(1+t*t)^2 = 0
```

This is a quartic. Use two half-circle charts: `beta=2*atan(t)` and
`beta=pi+2*atan(t)`, each with `-1 <= t <= 1`; the latter negates u and z. No infinite tangent
parameter or unbounded real-root search is needed. Divide lengths by their maximum extent
before forming products, so the polynomial cannot overflow merely because the curve is large.

Real roots on the closed interval can be isolated recursively by roots of the derivative.
Between consecutive derivative roots the polynomial is monotone, hence has at most one crossing;
opposite endpoint signs bracket it for bisection. Evaluate derivative roots as candidates too,
so an even-multiplicity/tangent root is not lost. This follows from Rolle's theorem and the
intermediate value theorem, not from an upstream IK branch.

Derivative extrema of squared radius similarly satisfy:

```text
(h+u*cos(beta)+z*sin(beta)) dot (-u*sin(beta)+z*cos(beta)) = 0
```

After the same substitution this is another quartic. Its candidates plus chart endpoints give
the minimum/maximum reachable radius. A continuous function on the connected circle attains
every radius between these bounds. Targets outside that interval use the nearest radial bound
for the rotation-only solution; the resulting residual is observable, not a numerical error.

All kernel arithmetic is binary64. The root evaluator uses a coefficient-scaled roundoff bound
to recognize tangent roots, and at most 80 bisection steps per interval. Candidate endpoints
must be checked geometrically; a polynomial cancellation is not proof of a valid solution.
Accepted radius candidates must be within `2e-7` times the normalization extent in radial distance.
The kernel endpoint accuracy is relative to geometry extent, including near tangencies;
conversion to binary32 introduces additional output rounding. Kernel tests cover scale changes
and generated reachable targets, separately from any legacy Golden.

### Bend choice and degeneracy

The bend sign is the sign of `cross(h, e(beta)-h)` multiplied by the sign of the outer frame
determinant. Thus it describes the two world segments, including reflection. Prefer roots
matching `bendPositive`; a zero cross product is neutral. If no root has the requested sign,
retain the best available geometric solution and report the actual residual. Within a sign,
choose the smallest sum of squared wrapped angular changes from the input pose, followed by
stable angle ordering for a deterministic tie.

For a zero target and a zero reachable vector, retain the input first angle; the direction
is underdetermined. If the second segment is wholly collapsed, retain its input angle and solve
only the first rotation. An entirely stationary chain has zero radial extent and retains both
angles. No absolute "short bone" cutoff changes a tiny nonzero bone into a zero-length one.

## Independent transverse scale and softness policies

For a nonnegative X multiplier `k` and mix `m` in `[0,1]`, use the effective factor
`f=(1-m)+m*k`. This form preserves a very small positive k at m=1 without cancellation in
`1+(k-1)*m`. Multiply the old signed X scale by f; preserve the old signs.

- Fixed transverse scale: old Y remains unchanged.
- Uniform: multiply old Y by f.
- Area: divide old Y by f. The local determinant is unchanged because `f*(1/f)=1` and
  the shear angles are unchanged. There is no compression floor or special 0.7 branch.
- At f=0, Area cannot preserve a nonzero area with finite axes; return an explicit numerical
  failure rather than inventing finite thickness. A positive result that underflows to zero
  or overflows binary32 also fails Area preservation. All candidate axes are prepared before
  any source pose is modified.

All modes also reject an unintentionally underflowed positive scaling result for a nonzero input
axis. Explicit zero compression remains available in Fixed/Uniform modes; it differs from losing
a positive result during conversion. Angle reduction takes the remainder before subtracting a
half turn, so huge finite whole turns do not lose the half-turn offset through rounding.

Softness is a distance in the solve frame. Let maximum reach be R, requested radius d,
`w=min(softness,R)` and `a=R-w`. With w=0, leave d unchanged. Otherwise:

```text
dSoft = d                                when d <= a
dSoft = a + w*(1-exp(-(d-a)/w))           when d > a
```

The value and its first derivative join continuously at a; the derivative thereafter is
positive and approaches zero, while the value approaches R. Use `expm1` for small arguments.
Large softness does not move the origin because w is bounded by R. The policy commutes with
uniform changes of distance units and has no hard epsilon activation gate.

Compression/stretch changes the reachable envelope first, then softness selects the effective
target within that envelope, then the rotation solve runs, then authored mix is applied once.
Softness may leave a visible residual even when stretching is enabled; it no longer silently
disables the stretch operation. The scale search and inheritance adapter are defined below.
For one bone, softness does not change the aim direction or chosen compression/stretch factor,
as there is no elbow approach to smooth. For longer chains it changes the effective target
before the last rotation solve.

## General forward-kinematic solve and scale search

The general path evaluates trial local poses using the runtime's existing authoritative
`compose_child_state` and root-matrix composition, with the outer parent captured once.
Only chain states are evaluated during search; descendants are refreshed at the final commit.
There is no replacement inheritance formula inside the optimizer.
The final transaction stages only the chain's local edits and the affected subtree's world
states. It validates every affected descendant before either map is written, preserving earlier
world changes outside that subtree. It does not copy the full skeleton for every constraint.

Use bounded coordinate minimization of world endpoint distance. Angular coordinates are
periodic on `[-180,180]`. For each coordinate, evaluate 32 uniform intervals plus the current
value, refine the two best neighborhoods with 40 golden-section steps, and retain the best
finite candidate including the unchanged value. The golden fraction solves the self-similar
interval relation `g*g=1-g`. Reverse joint order makes the distal joint adjustable first.
Two-bone searches try the authored child angle and quarter/half-turn offsets; select a solution
with the requested world bend when multiple solutions meet the requested residual threshold.
Otherwise choose the smallest observed residual, with stable seed/coordinate ordering.
This is a bounded iterative solve, not a claim of a global optimum for every inheritance mode.
One/two-bone general solves use at least one pass; validated constraint data requires a positive
iteration limit. Longer authoring chains obey their supplied iteration limit, including zero.
`iterationsUsed` is the winning search seed's completed pass count (or 1 for an analytic solve),
not the aggregate number of FK evaluations. The separate visit budget bounds aggregate work.

The first integrated test exposed slow coordinate convergence for a reachable Normal /
TranslationOnly pair (world residual 0.053 after 48 passes). To handle coupled axes, each pass
also takes one independently derived damped least-squares correction from actual FK samples.
Let `e=tip-target` and let the two-row Jacobian J be central finite differences (0.05 degrees
for angles, 0.001 for log scale, clamped at the allowed bounds). Solve
`delta=-J^T*(J*J^T+lambda*I)^-1*e`, with `lambda=1e-6*trace(J*J^T)`.
The positive diagonal term makes rank-deficient directions bounded; a zero Jacobian does not
move. Cap a proposed correction to 30 degrees / 1 log-scale unit, then try up to 10 halvings,
accepting only a finite decrease of actual endpoint error. The 2x2 inverse follows directly
from minimizing `|J*delta+e|^2+lambda*|delta|^2`; no inheritance derivative is reimplemented.
All difference and line-search evaluations count toward the same bone-visit budget.

First solve with the authored scales. If it cannot meet the threshold and compression or
stretching is enabled, a one/two-bone solve adds the first bone's logarithmic X multiplier as
another coordinate. Compression permits log factors below zero; stretching permits those above
zero. Bounds come from finite, nonzero binary32 axis representability and the selected transverse
mode. Preserve scale signs. Rotation-only solutions that already reach the target do not acquire
an unnecessary scale degree of freedom. Longer chains retain their rotation-only authoring solve.

The ordinary affine pair kernel provides a seed and exact radial solutions. Normal one-bone
aiming uses the target in the complete parent frame and its signed local X-axis/shear. All other
cases, including singular outer frames, use the actual forward-kinematic search. No parent shear
or child translation is cleared, and a no-scale child still participates through its true transform.
An accepted ordinary pair uses the nearest radius in its solve frame even when an anisotropic
outer frame means this is not the globally closest world-space endpoint. Normal single-bone
aiming similarly preserves the target ray; it does not substitute an ellipse-distance optimizer.
The polynomial candidate must agree with actual binary32 FK within `64*epsilon*extent + 2*tiny`,
where epsilon is binary32 machine epsilon, tiny its smallest positive subnormal, and extent is
the greater of the predicted world coordinate magnitudes and evaluated world segment-length sum.
Otherwise the general solver supplies the result. This bound covers FK rounding, not target error.

After solving scale, apply softness and solve rotation for that effective target. Ordinary affine
pairs use their polynomial maximum radius; the general path defines its softness reference as the
sum of world segment lengths in the unsoftened solved pose. This is a declared pose-dependent
reference, not a claim of a global maximum over all possible inherited poses. Convert the softened
world target back to the regular rotation solve without adding further scale degrees of freedom.
Mix original and fully solved rotations/scales once at the end; Area mode uses the reciprocal of
the mixed X factor, so partial mix retains its area invariant.

Each solve has a maximum of 1,000,000 forward-kinematic bone visits. Count unsuccessful trial
evaluations too. Budget exhaustion is a structured failure. Unrepresentable exploratory trials
lie outside the feasible numeric domain and cannot replace a valid candidate; invalid source poses
or an unrepresentable final result fail. Finite unreachable targets publish their residual.

## Adapter, failure and compatibility requirements

- The adapter must use the authoritative runtime affine/inheritance evaluation. Do not introduce
  a second world-transform implementation in editor, API, renderer, or the math kernel.
- Ordinary affine pairs may use the polynomial reduction. Nonstandard inheritance, singular
  outer frames and longer chains retain solving support through a bounded solver using actual
  forward kinematics. They must not be made successful no-ops merely to avoid difficult cases.
- Check candidate geometry with actual forward kinematics before publication, including very
  large authored shear/rotation values where binary32 angle addition can lose a small rotation.
  The algebraic rotation/shape factorization alone is not sufficient evidence in those cases.
- All trial poses, scales and world states must be local candidates. Numerical failure or resource
  exhaustion must propagate through the Runtime API and leave state/frame unchanged; finite
  unreachable targets instead return the best finite pose with residual diagnostics.
- Keep existing native data readable. Loading legacy exported `constraint.ik` must not silently
  run the new rules. Add an explicit profile gate, combined migration warning and feature bit;
  do not downgrade existing Path/Physics profiles.
- Preserve the existing language-neutral frame and renderer boundary. Stage and independently
  explain every conformance change before updating a suite or restoring a capability declaration.

## Acceptance matrix

1. Analytic 3/4/5 arms, both bends, inner/outer unreachable targets, and tangent/double roots.
2. Full shear, nonuniform/negative scales, nonzero child Y, reflected outer frames, and targets
   generated independently from known rotations; show endpoint residual and preserved inputs.
3. Zero/tiny/huge geometry, collapsed segments, chart boundaries, repeated/constant polynomials,
   and deterministic multiple-solution selection.
4. Area invariant under compression/stretch/mix, original signs, near-zero factors, non-finite
   and unrepresentable results, with no partial source writes.
5. Softness value/derivative continuity, monotonicity, unit scaling, extreme finite parameters,
   and combination with mix/compression/stretch.
6. Each inheritance mode, multi-bone budget/threshold/early termination, failed-operation rollback,
   migration/JSON/CANEB gates, independent conformance and relevant CPU measurements.

Each language Core owns IK dispatch, forward composition and bounded search. Constraints
propagate `nonFinite`/`resourceLimit` with the constraint identity. Engine adapters consume
the resulting world geometry and do not implement a second solver.
