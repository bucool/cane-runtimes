# Cane Runtime specification

`formats/` defines project, Runtime JSON, CANEB, Atlas and canonical data formats.
`runtime/` defines the language-neutral Runtime API and its animation, geometry,
affine, constraint, Physics and procedural-pose algorithms.

The target is Runtime Format 1.1 and Runtime API 1.13. The repository's
`spec-lock.json` also identifies the reference conformance suite and manifest
digest; it describes the compatibility target and is not execution evidence.
Test runners, fixture data and local verification reports are outside the public
source distribution and are not Runtime build dependencies.

The normative algorithm and format files define the current contract.
Bone setup and animated scales may be finite zero values and retain singular
world geometry. Region scale validation follows its own format rules. The
[earlier scale clarification](corrections/bone-animation-zero-scale-20260908.json)
records the prior 1.0 amendment; the 1.1 Runtime Model defines current admission.
Implementation and rendering boundaries are in [the architecture guide](../docs/ARCHITECTURE.md).
