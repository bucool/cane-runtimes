# Cane C# and Unity

The native C# v2 runtime targets existing JSON and `.cane2` exports, the current
TS Core capability set, and Unity 6000.3.23f1.
It does not load historical runtime formats or use an FFI implementation.

In Unity Package Manager, install Core first and the base Unity package second,
then any optional adapters. Use **Add package from disk** with each package's
`package.json` in a source checkout, or **Add package from tarball** with the
corresponding `.tgz` delivery files. The package guides below describe usage and
the required Unity modules.

`cane-core` targets .NET Standard 2.1 and owns decoding, animation, constraints,
queries and final geometry. [cane-unity](cane-unity/README.md) owns Unity resources,
scene/Canvas rendering and lifecycle. Unity does not re-evaluate Core output.
The optional [URP package](cane-unity-urp/README.md) contains scene shaders;
the base Unity package does not depend on URP.
The optional [UI Toolkit package](cane-unity-uitoolkit/README.md) provides a
UXML control over the same final geometry and player, without a per-character
RenderTexture.
The optional [Addressables package](cane-unity-addressables/README.md) shares
on-demand texture loads and placeholder resources across players.

Core behavior is checked against the TS/Rust references and original animation
exports. Unity regression coverage includes scene/Canvas/UI Toolkit rendering,
skins and attachments, native masks, Animator, Timeline, root motion, bone
drivers/followers, Physics, atlas packing, lighting/effects and streamed resources.
The validated Unity profile is Windows x64 Mono, D3D11, Gamma and Linear, with
Built-in and URP 17.3. The packages retain their pre-1.0 version identifiers.

See the [performance profile](docs/PERFORMANCE.md) for measured complete-frame
latency, cold loading, actual Player size and the comparison methodology.
