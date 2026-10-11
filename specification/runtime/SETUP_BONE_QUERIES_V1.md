# Setup and local-pose queries

These are renderer-neutral Runtime API queries. Their language projections are
listed in [Playback API](../../docs/PLAYBACK_API.md#setup-queries).

The setup query evaluates exactly the supplied hierarchy under identity host
transform. It preserves supplied bone order and includes every bone. Empty input
returns empty geometry. Bone local values may be finite zero, positive or negative
scale; the ordinary inheritance equations still apply. Invalid identities,
missing parents and cycles fail without producing a partial publication.

The inverse query accepts an optional parent affine, a desired world affine,
inheritance mode and reference local pose. It recovers the nearest representable
local channels, preserving the reference shear convention and signed-scale
branches where possible. Rank-deficient parents use the checked closest-preimage
rules in [Transform channels](TRANSFORM_CHANNELS_DESIGN_V2.md). Forward composition
must recover the requested matrix within those rules' binary32 roundoff bound.
A null parent selects ordinary identity-root composition regardless of mode.

Outputs are owned values. Neither query mutates a player or executes rendering,
file I/O, skin selection, animation sampling or constraint solving.
