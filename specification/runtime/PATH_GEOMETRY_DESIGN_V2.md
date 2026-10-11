# Cane Path geometry and placement v2

## Requirements

The product requirement is to place bones along a piecewise cubic path, with distance or
fractional positions, four spacing units, three orientation/scale modes, and an inverse
world-point query. Open paths extend along endpoint tangents; closed paths wrap.
Authored setup-length parameterization remains a separate supported mode.

## Cubic geometry and independent error bound

For control points P0..P3, define the cubic by three nested linear interpolations
(de Casteljau evaluation). Its derivative is the quadratic Bernstein interpolation of
3(P1-P0), 3(P2-P1), 3(P3-P2). Splitting the interpolation triangle at u yields exact left
and right subcurves in real arithmetic; no resampling or curve fitting is involved.

Let L be the endpoint chord length and U the sum of the three control-polygon edge lengths.
Then L <= arc length <= U. The lower bound follows from the triangle inequality along the
curve. For the upper bound, integrate the norm of the quadratic derivative, apply the
triangle inequality, and note that each quadratic Bernstein weight integrates to 1/3.
The same proof applies to every subcurve. Summing bounds preserves their ordering.

Subdivision bounds are also described in Gravesen (1997),
[author institution's abstract](https://orbit.dtu.dk/en/publications/adaptive-subdivision-and-the-length-and-energy-of-b%C3%A9zier-curves/),
DOI 10.1016/0925-7721(95)00054-2. Only that abstract was read; no paper code or
third-party implementation is used. The preceding proof is sufficient for the bound used here.

### Preparation and distance inversion

- Convert finite binary32 controls to binary64 for geometry. This avoids binary32 square
  overflow and makes subdivision roundoff small compared with output precision.
- Let epsilon = 1e-6 * U for the original cubic. A stationary cubic has zero length.
- Subdivide at the midpoint until each leaf satisfies U_leaf-L_leaf <= epsilon*(t1-t0).
  The sum of gaps is therefore at most epsilon in real arithmetic. Use (L_leaf+U_leaf)/2
  as its length estimate. Store leaf parameter bounds and cumulative estimated length.
- A leaf that has a straight image can still have nonlinear parameter speed. Never infer
  its parameter by linearly interpolating distance. Locate a leaf by cumulative distance,
  then bisect its parameter for 48 iterations using the bounds of the partial subcurve.
- The partial length estimate uses the same midpoint of bounds. Prefix and partial errors
  are bounded by the subdivision gaps; floating-point output additionally incurs binary32
  rounding. Geometry error scales with curve extent, not with translation from the origin.
- Prepared lengths and world outputs must be representable as finite binary32 values at the
  Runtime API boundary. Failure is explicit, not an omitted constraint or a saturated value.

Preparation may visit at most 1,000,000 subdivision nodes across the curves of one resolved
Path. Maximum depth is 32. Reaching a bound without satisfying the error requirement returns
`resourceLimit`; it does not accept a coarser answer. Independent implementations may choose
different internal methods if they meet the geometric accuracy and public failure contract.

### Closest-point inverse

Use cubic control boxes as spatial lower bounds. Every subcurve lies in the convex hull of
its controls and therefore inside their axis-aligned box. Distance from the target to this
box cannot exceed distance to any point on that subcurve.

Maintain evaluated endpoint/midpoint candidates as upper bounds and subdivide intervals whose
lower bound could improve the current distance by more than 1e-6*U. Traverse earlier parameters
first; equal evaluated distances choose the smaller parameter. This is a deterministic candidate
tie rule, not a promise to identify the smallest exact parameter among all numerical near-ties.
The separate projection budget is 1,000,000 visited nodes across one resolved Path. Budget or
depth exhaustion is explicit. A stationary cubic returns parameter zero.

Convert the winning parameter to arc distance in constant-speed mode or to the authored setup
domain in parameterized mode. Include the two endpoint rays for open paths. Forward and inverse
queries must share the same geometric model. A fixed 24-segment polyline is not an accuracy oracle.

Endpoint orientation uses the first nonzero derivative order: first derivative, then second
(negated at the end for the incoming direction), then third. A wholly stationary cubic has
deterministic +X orientation. This also handles coincident endpoint handles without a false
missing-path result. At an interior stationary point use the outgoing direction.

## Path domain and bone placement

Constant-speed mode concatenates the new arc estimates. Setup-parameterized mode retains each
validated authored cumulative length; within a curve its domain maps linearly to cubic parameter.
Stored lengths are not silently regenerated. Format-domain fallback generation follows the
24-chord compatibility policy below and must not introduce a format-to-runtime dependency.

### Missing setup lengths: numerical correction, 2026-09-24

This policy is recorded before its implementation. Retain the existing 24 equal parameter
intervals per cubic, open-path final-length duplication, closed-path wrap and one entry per
knot. This is a bounded-cost approximation, not the adaptive constant-speed accuracy contract
above. The number 24 has visible local history but no verified original accuracy rationale;
retain it for compatibility, not as evidence of authorship or copying.

The independent definition is the sum of distances between B(i/24) for i=0..24. Bernstein
weights sum to one, so B(t)-P0 is the same polynomial on the controls Pi-P0. Evaluate these
local controls and accumulate distances in binary64, promoting before subtraction. Never
add P0 back merely to subtract adjacent samples: absolute position is irrelevant to length.
A stationary cubic must return exactly zero even near the binary32 coordinate limit. Narrow
only cumulative output entries, rejecting totals outside finite binary32. Invalid layouts,
nonfinite controls and fewer than two knots still fail through the existing Option boundary.

No stored table is rewritten on load. Only explicit authoring recalculation, export of
missing lengths and runtime's existing missing-table fallback use the corrected estimator.
Their roundoff can change and large translated shapes no longer gain spurious length.
The real-valued 24-chord definition, native profile and Runtime API version remain unchanged;
this is a numerical defect correction. It does not recover detail already lost in input f32
coordinates or claim the estimator has the adaptive kernel's relative error bound.

Spacing is independent of approximation internals:

- Fixed: authored spacing times the previous bone's world/setup length ratio, when defined.
- Length: the nonnegative authored bone length plus spacing, scaled by that ratio.
- Percent: spacing times total path-domain length.
- Proportional: distribute spacing times total domain length by participating world bone lengths;
  define and test the existing zero-length fallback separately.

Each bone's anchor is sampled independently at its accumulated path position. Tangent mode aims
along the local path tangent. Chain mode aims along the chord to the next anchor. ChainScale
also scales X to that chord length. Rotation offset is reflected with the target frame.
Blend each requested world property using its authored mix; preserve other properties.
Prepare all desired states before committing in hierarchy order.

There is no zero-offset-only feedback of a fully rotated previous bone tip into the next anchor.
Changing a rotation offset through zero must be continuous. At partial mix, exact joint contact
is not a separate guarantee: anchors, rotation and scale have explicitly independent mix behavior.
This changes legacy Chain output and requires a new semantic profile and migration notice.

## Version and failure integration

Contract: API 1.5, feature `constraint.path.geometry-v2`. Legacy `constraint.path`
remains decodable but is not silently executed under the new semantics. Native authoring export
opts into the new profile. Migration preserves authored controls, lengths and parameters, warns
that constant-speed timing/placement and Chain blending can change, and requires review.
Combined Physics/Path migration must explicitly cover both changes and never downgrade an
already newer profile. JSON/CANEB container versions and renderer-neutral frame layouts stay unchanged.

Runtime preparation/geometry errors propagate through runtime-api as `nonFinite` or
`resourceLimit`. An operation must not publish a partial pose/frame. Absence of an active Path
attachment is a distinct successful no-op, not a way to hide a numerical failure.

## Independent validation required

- Line with uniform controls; nonlinear-speed straight cubic B(t)=(t^3,0), so distance d gives
  world point (d,0) rather than (d^3,0).
- P=(0,0),(0,1),(1,1),(1,0): speed 3-6t+6t^2, arc s(t)=3t-3t^2+2t^3, total 2.
- Endpoint/derivative degeneracies, stationary curves, retracing collinear curves, closed paths,
  multiple curves, reflection, translation, large and tiny finite scales.
- Independent high-order quadrature checks on self-authored curves; subdivision bounds, monotonic
  arc distance, forward/inverse accuracy and consistent endpoint rays.
- Budget failures, non-finite/overflow failures and state/frame rollback.
- Hand-derived bone anchors for all spacing/orientation modes and continuity through zero offset.
- Explicit migration and JSON/CANEB/API gates; each affected Golden reviewed individually.
- Representative Path CPU cost, reported separately from canvas/render performance.
