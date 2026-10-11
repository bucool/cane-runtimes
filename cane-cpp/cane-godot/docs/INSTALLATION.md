# Godot installation

Download the Windows x64 addon ZIP from [GitHub Releases](https://github.com/bucool/cane-runtimes/releases/tag/v0.1.0-preview.1) and
extract it at your Godot project's root. It contains the native DLL and
`addons/cane` editor integration. No .NET installation is needed.
To build the same extension yourself, follow the
[source build commands](../../../docs/BUILDING.md).

Follow the
[editor workflow](EDITOR_WORKFLOW.md) ([中文](EDITOR_WORKFLOW.zh-CN.md))
to import an animation, drag it into a scene and preview it.

In your Godot project, create this layout:

```text
addons/cane/
  cane.gdextension
  plugin.cfg
  editor/
  bin/cane-godot.dll
```

Copy `cane.gdextension`, `plugin.cfg`, `editor/` and the compiled DLL into the
layout above. Restart the editor after installing or
replacing the native binary; the descriptor disables hot reload. Enable Cane in
**Project > Project Settings > Plugins** to activate importing and Inspector
preview. The plugin enables HDR 2D in the project and the editor's 2D viewport.

Use ordinary Godot **4.7.2** on Windows x64. Both Compatibility/OpenGL and
Forward+/Vulkan are verified on that engine version. The DLL includes the
independent C++ Core and does not require C# or Mono. Enable
**Rendering > Viewport > HDR 2D** for the adapter's color contract. Preserve the
relative paths of Cane Runtime JSON/CANEB, Atlas and image resources.

The extension is compiled against the Godot 4.5 extension ABI. That ABI minimum
does not mean every older engine's renderer is supported. Godot 4.5 and 4.5.2
Compatibility can crash at an exact 16,384-draw backbuffer boundary, including
in projects without Cane. Use 4.7.2 for the complete tested workflow.

The source configuration targets Windows x64. Use the pinned Godot 4.5
`godot-cpp` dependency and the
[source build commands](../../../docs/BUILDING.md). The extension descriptor
does not declare other platforms.

Distribution builds use `CANE_GODOT_REPRODUCIBLE_PATHS=ON` to replace local source
and build directories in native diagnostic strings with portable paths. This is
optional for source users; the Windows distribution uses MSVC 14.44 with its
deterministic path-mapping support. It does not change the runtime data format.

Use [the SDK API](SDK.md) and [playback controls](PLAYBACK.md) from GDScript. See [resources](RESOURCES.md), [Bone followers](BONE_FOLLOWERS.md),
[Slot nodes](SLOT_NODES.md) and [bounds](BOUNDS.md) for integration.

Prefer **Forward+** for efficient linear rendering. Compatibility is supported,
but needs additional destination copies for correct linear blending on its sRGB
canvas. Both paths require HDR 2D. When saving viewport captures as PNG, unpremultiply
RGB first; then convert linear RGB to sRGB on Forward+/Mobile. Compatibility's
readback is already sRGB and must not receive a second conversion.
