# Independent polar inheritance v2 design

## Requirements

All five modes remain available. The child's origin follows the full parent affine transform;
the selected mode filters only the inherited linear axes. Root application and normal affine
composition keep their current coordinate contract. There is no new renderer or editor policy
inside runtime, no forward solver in an editor overlay, and no implicit change to old exports.

The new inheritance basis depends only on the parent matrix and mode. Local rotation, signed
scale and shear are then applied once through the ordinary local matrix. This avoids a
child-rotation-dependent parent basis and makes forward composition and inverse compensation
use the same explicit linear operator.

## Independent mathematics

Use the standard real polar decomposition. The mathematical reference is
[Nick Higham, What is the Polar Decomposition?](https://nhigham.com/2020/07/28/what-is-the-polar-decomposition/):
orthogonal and positive-semidefinite factors separate orientation from stretch. This document
uses the **left** factorization `P = S Q`, where `S = sqrt(P P^T)` is symmetric PSD. It is chosen
so that stretch is expressed along the existing world axes. The formulas below are derived
for two dimensions, not translated from an animation runtime.

For `P = [[a,c],[b,d]]`, maximizing the Frobenius inner product with a rotation gives:

```text
<P,R(theta)> = (a+d)*cos(theta) + (b-c)*sin(theta)
```

The optimum unit pair is `(a+d,b-c)/hypot(a+d,b-c)`. Let its rotation be R. For an improper
orthogonal matrix `F = [[cos(theta),sin(theta)],[sin(theta),-cos(theta)]]`, the same derivation
uses `(a-d,b+c)`. Let its optimum be F. Select `Q=R` when `det(P)>=0`, otherwise `Q=F`.
Then `S=P Q^T`; its off-diagonal entries are averaged to enforce numerical symmetry.
For real 2x2 inputs the two objective values differ by the sign of `det(P)`, so this is the
orthogonal polar factor for full rank. It is also a deterministic extension at rank loss.

IgnoreRotationAndReflection retains S. IgnoreScale retains Q. IgnoreScaleAndReflection uses the closest
**proper** rotation R, regardless of determinant. Choosing the closest proper rotation is a
separate, explicit product policy; it is not always obtained by flipping an arbitrary Q column.

| Mode | Inherited axes B | Child axes |
| --- | --- | --- |
| normal | P | P L |
| onlyTranslation | I | L |
| noRotationOrReflection | S | S L |
| noScale | Q | Q L |
| noScaleOrReflection | R | R L |

The child's world origin remains `P * localOrigin + parentOrigin` in every mode. Translation is
not accidentally passed through B. The host root retains its current place in the FK chain;
this change does not separately invent host-root compensation rules.

## Rank, ambiguity and numeric policy

- All input coefficients are finite binary32. Convert them to binary64 before arithmetic.
  Products of binary32 magnitudes, including their squares, fit the binary64 exponent range;
  very small nonzero axes must not be removed by a world-unit epsilon threshold.
- Compute determinant from the original binary32 values widened to binary64. Do not first
  scale/round them, which could change the sign of a nearly rank-one matrix.
- At rank one, select the proper orthogonal completion. S remains rank one. At zero, S is zero
  and both orientation factors are identity. Thus IgnoreScale modes keep the child's own axes even
  when the parent is completely collapsed; the child's origin still follows the collapsed parent.
- If `(a+d,b-c)` is exactly zero, every proper rotation has the same objective value. Select
  identity. This includes isotropic reflections; no continuity across a mathematically
  non-unique optimum is promised. There is no epsilon-dependent tie selection.
- Keep B in binary64 until multiplication with the child's axes. A stretch coefficient may be
  greater than binary32 MAX even though its product with a small child axis is representable.
  Narrow only final public geometry with finite/representability checks, never by clamping.
- Invalid inputs and unrepresentable final poses must produce structured failure through the
  public sampling transaction; a missing bone or partially published frame is not success.

## Independent analytic cases

### Inverse and representability contract

The inverse uses the same inherited operator B, never a second inheritance formula or a
child-angle search. Recover the origin through the full P. For a full-rank matrix the solution
is unique. At rank one choose the reachable solution nearest the reference local vector:
`u = u_ref + B^+ (wanted - B u_ref)`. This preserves the unobservable null-space component.
For zero B only zero axes are reachable. Orientation-only Q/R factors are orthogonal even
when P loses rank, so their inverse is simply their transpose and every finite axis is reachable.

For left stretch, use `S^+ = Q P^+`, and for full rank `S^-1 = Q P^-1`.
Determine P's rank from its original widened binary32 determinant, not a determinant of the
rounded/symmetrized S. A rank-one P has `P^+ = P^T / ||P||_F^2`. The exact inverse and
rank-one projection follow ordinary linear algebra; no threshold discards a small nonzero axis.
Intermediate factors remain binary64, including cases where S itself cannot fit in binary32.

Retain the reference shear-X and scale signs while recovering rotation, signed axis lengths
and shear-Y. A zero recovered axis is represented explicitly; its undefined angle keeps the
available reference channel. Roundoff-sized perpendicular residuals of binary32 target axes
are admitted using the existing checked reconstruction envelope (64 binary32 epsilons times
the axis magnitude, with one smallest subnormal as a floor). A genuinely unreachable target,
non-finite value or failed forward reconstruction is an error. This tolerance is a verification
envelope, not a changed definition of rank or a reason to project arbitrary requests silently.

Normal and TranslationOnly keep their existing binary32 forward operation order. The latter
keeps its existing quarter-turn evaluation of the Y axis. The three changed modes use ordinary
local axes and widened B multiplication and origin arithmetic, narrowing final components only.
All public derived pose fields must also be representable. A failure in an initial FK, a Slider
resample or a constraint descendant must propagate through the existing staged operation,
leaving the last published frame, player configuration and clocks intact.

For a parent `R(90) diag(2,3)`, S is `diag(3,2)` and Q=R(90). For the unit shear
`P=[[1,1],[0,1]]`, `Q=[[2,1],[-1,2]]/sqrt(5)` and `S=[[3,1],[1,2]]/sqrt(5)`.
These cases distinguish world-space stretch from right stretch and verify the shear policy.

For `P=diag(-2,3)`, Q is `diag(-1,1)`, S is `diag(2,3)`, and R is identity. With
`diag(-3,2)`, the closest R is instead a half turn. For `[[0,2],[2,0]]`, the proper factor
is tied and must be identity. These intentionally exercise different reflection policies.

For rank-one `P=[[3,0],[4,0]]`, `S=[[9,12],[12,16]]/5` and
`Q=[[3,-4],[4,3]]/5`. A zero first column and a zero matrix require separate cases.
Additional tests check `Q Q^T=I`, PSD symmetry, `S Q=P`, `S S=P P^T`, positive-scale
invariance, near rank-one determinant signs, invalid inputs, subnormal axes and binary32 MAX.
A small integer matrix grid provides independent algebraic properties, not captured Golden.

## Compatibility and integration

`bone.inheritance.polar-v2` requires Runtime API 1.8 and capability bit 46.
Setup and animated use of the three special modes require the profile. InheritAll and
TranslationOnly alone do not raise the behavior minimum. Missing profile declarations are errors.
Forward composition and applied-local recovery use the same parent-derived basis. Singular
origin recovery uses the full parent and preserves the reference null-space component.

Setup queries include every authored bone, omit animation/constraints/skin filtering, and reject
invalid hierarchies atomically. The owned setup frame and inverse-query contract is defined in
[setup-bone queries](SETUP_BONE_QUERIES_V1.md). Editor commands and undo remain host responsibilities.
