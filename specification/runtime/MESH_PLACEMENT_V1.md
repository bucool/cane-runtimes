# Mesh placement (Runtime API 1.13)

Status: normative extension, selected by the required feature `mesh.placement`.

## Purpose and stored data

A Mesh may store `placement: {x, y}` to move the whole image without changing
its setup vertices, weights, bind inverses, bones, constraints, or deform keys.
Both coordinates are finite binary32 values in skeleton distance units. The
default is `{x: 0, y: 0}`; canonical writers omit that default field.

Placement is a translation after skeletal deformation. It is not an additional
bone-local or slot-local vertex delta, and it is not a renderer-only modifier.
Project JSON, Runtime JSON and CANEB preserve the same field. Project scaling
scales both coordinates by the project scale factor.

## Evaluation

Let `W_i(t)` be the existing authoritative world position of source vertex `i`
after animation, constraints, deform and normalized weighted skinning. Let
`p=(placement.x, placement.y)` and let `H` be the host instance root transform.
The final position before clipping is:

```text
d = Linear(H) * p
placedWorld_i(t) = W_i(t) + d
```

The host root translation is not added to `d`. Animated root bones, owning-slot
bones, influence bones, and their constraints do not transform `p`; their motion
is already included in `W_i(t)`. With the identity host root, every original
frame is translated by exactly the same authored vector. Changing the host's
rotation, scale or reflection transforms the placement along with the character.
The same equation applies to unweighted Mesh attachments after slot projection.

All source vertices receive the same `d`, so differences between vertex pairs
and the original animation trajectory relative to the placement are preserved.
Placement does not replace, reset, resample or rebase the deformation animation.

The reference evaluates the host-linear products and their sum in binary64,
then narrows the vector to finite binary32 coordinates. Source-world queries
add that vector to the existing world points. Rendering preserves its existing
skinned vertex buffer and affine basis, then adds the vector to the affine's
translation before final projection. These additions and inverse subtraction
use binary64 intermediates and finite binary32 results. The paths are
mathematically equivalent within the existing finite-precision geometry
tolerance; this feature does not change the skinning/projection precision
profile. An all-zero placement uses the existing path without extra arithmetic.

This stage runs before skeleton clipping and Atlas trim. Clipping therefore
intersects the placed geometry with the existing clipping attachment. The
clipping attachment itself, other meshes and bones are not moved. Bounds,
picking and rendering consume the resulting authoritative geometry. Renderer
adapters must not apply placement again.

## Linked meshes

The displayed attachment owns its placement, independently of its geometry
source and deform owner. Resolving `link.parentMeshId` still supplies the same
vertices, UVs, indices, weights, bind inverses, edges and hull. `inheritDeform`
keeps its existing meaning. Placement is never accumulated through the link
chain: moving the source Mesh does not implicitly move linked instances, and
moving an instance does not edit its source or siblings.

## Source geometry and inverse authoring queries

`setupVerticesXy`, `sampledVerticesXy` and `deformValues` retain their existing
source-local meaning. The source index order and buffer lengths are unchanged.
`setupWorldVerticesXy` and `worldVerticesXy` include displayed placement; the
former still omits sampled deform. These queries bypass clipping and Atlas trim
as before, so rendered intersections never replace editable source vertices.

For a world target `T`, each mesh vertex-position inverse, weighted-influence
inverse and weight-local-position inverse first computes `T-d`, then invokes
the existing inverse skinning algorithm. Logical-position-to-deform conversion
does not subtract placement because its input is not world-space. Batched
queries, linked instances, setup queries and runtime resource overlays follow
the same rules. A query never changes player clocks, published frames or data.

## Compatibility

Any nonzero placement requires `mesh.placement`. Runtime exports additionally
require Runtime API 1.13 or newer. Readers that do not support the feature must
reject the document before playback. Files with only identity placement keep
their former minimum version and required feature set; they retain previous
geometry behavior and canonical encoding. The JSON/CANEB container version and
normalized Frame/RenderPacket layouts are unchanged.

Runtime-created attachment overlays containing nonzero placement raise their
derived immutable project view to API 1.13 and declare `mesh.placement`; the
shared source document remains unchanged. Other independent runtimes must add
this feature before accepting such exports. Third-party export formats have separate capability requirements.

