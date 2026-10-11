# Track v2 channel projection and endpoint operations

This supplements the [pose design](TRACK_POSE_COMPOSITION_DESIGN_V2.md) for API 1.10.
No renderer or editor state enters this projection.

## Sampling boundary

Project each real Cane clip to a sparse, typed set of local channels. A stable key consists of
owner kind, owner ID and field. Setup supplies the schema and values; sampled absence is distinct
from an explicit setup-valued key. Curves use the existing independently replaced roots-v2 sampler;
the old `AnimationPropertyMix` and old per-layer blend functions are not called.

| Owner | Channels |
| --- | --- |
| Bone | X/Y, rotation, scale X/Y, shear X/Y, inheritance choice |
| Slot | attachment placeholder choice, light RGB, alpha, dark RGB and dark-present choice |
| Attachment | Region X/Y/rotation/scale X/Y; canonical deform vector; sequence index |
| Constraint | All keyed IK, Transform, Path, Physics and Slider fields; boolean IK fields are choices |
| Skeleton | selected skin-ID list and draw-order operation sequence |

Paired/scalar bone keys retain exact-time scalar precedence. Before the first applicable key the
channel is absent. Separate slot alpha keys take priority over RGBA's alpha for that timeline;
if the separate alpha starts later, alpha is absent until then, rather than writing setup alpha.
No disabled color-alpha curve is evaluated. RGB values are normalized scalar components; final
color-string encoding/quantization belongs to the later publication adapter.

Inheritance, attachments, sequence, skins and boolean fields are opaque choices for composition.
The current skin mapping/active-set rules are applied after composition, not reimplemented here.
Dark presence is a discrete choice at the candidate's threshold, while its RGB channels use zero
when absent. This differs from the old layer's OR-of-dark-presence behavior and needs explicit
versioning and public output tests before rollout.

Attachment and draw-order eligibility are separate sampler inputs. Disabled families produce
absent channels; they must not evaluate their timeline values. Their per-entry/progress calculation
and propagation through interrupted chains remain separate integration work. Scalar/vector channels
are independent of these switches. A future zero-opacity fast path must retain authored-presence
information needed by explicit hold without evaluating disabled curves.

## Draw-order operation rather than a frozen pose

A folder changes only a subset of slots. Sampling it into a full setup-order list would erase
unrelated lower-track changes. Therefore a draw-order sample is an ordered sequence of permutation
patches. A full draw-order key contributes a patch containing every slot; folder keys contribute
only their controlled subset, in declaration order. Inactive (before-first-key) folders are absent.

For a patch [p0,...,pk], find those same elements in the underlay order and replace only their
occupied positions, in ascending position order, with the patch sequence. All other positions
stay unchanged. Unique membership proves the result remains a permutation. Reuse of an element
across successive patches is allowed; a duplicate inside one patch or an unknown element is an
error. An empty patch sequence is a sampled no-op, distinct from an absent channel.

At clip opacity >= 1/2, apply the operations to that endpoint's own underlay; below 1/2 keep the
underlay. Additive uses the same discrete policy. After forming both complete endpoint orders,
transition interpolation selects the outgoing order below progress 1/2 and incoming otherwise.
Thus the pure composer gains permutation and permutation-patch value kinds. Patches are sample
operations only, never setup/pose values. Count schema validation, elements and each full-order
scan against the existing work budget before allocation/processing.

Example: lower order [B,A,D,C], folder patch [C,A] controls A and C. It yields [B,C,D,A]; B and D
retain their positions. A later patch [D,B] yields [D,C,B,A]. No sample-time setup reset is involved.

## Bounds and deform spaces

Bounded **pose** channels stay inside their range. A curve may overshoot that range, so bounded
**sample** values may be outside it while remaining finite and using identical bounds. Apply the
wide arithmetic first and clamp the complete endpoint, as required by the original candidate
equation. Do not clamp targets before opacity or reject a finite overshoot just because it is a
sample. The candidate validator is extended accordingly; setup remains strictly bounded.

Colors/alpha and Physics inertia/damping/mix use [0,1]. Physics mass uses the smallest positive
f32 through f32::MAX, retaining a positive endpoint floor; other constraint coefficients remain
ordinary scalars, including permitted extrapolating mixes. Wildcard Physics tracks affect only
fields whose setup global flags are enabled. Specific/global timeline declarations retain their
input order for each field. Physics resets are events, not interpolated channel values.

Each deform value is converted to its attachment's canonical space before interpolation:
unweighted local vertex positions, or weighted influence offsets in stable influence order.
Use the existing reviewed geometry inverse and linked-geometry resolution; do not invent a second
inverse or skip failed canonicalization. Setup and sampled vectors share an attachment-local
layout identity. Shape errors, failed conversions and non-finite values must be explicit errors.
Final geometry, active skin resolution and constraint solving remain in their existing domains.

## Verification requirements

Cover each channel family, exact key ties, before-first absence, separate alpha, wildcard flags,
linked/weighted deform, discrete gates, partial sorting and failures. Sparse samples flow through
the local schema and reversible choice table into the endpoint composer. Tests must observe
published frames, not only intermediate graphs.
