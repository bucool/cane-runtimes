# Color blending

`CaneSkeleton.blend_color_space` defaults to **Gamma** (`0`). Set **Linear** (`1`)
in the Inspector or at runtime without reloading the animation:

```gdscript
$CaneSkeleton.blend_color_space = 1 # Linear; 0 selects Gamma.
```

Keep **HDR 2D** enabled. The adapter handles the color-space boundary without
changing Runtime data or texture metadata. It converts canonical source pixels
before filtering, uses tint in the selected space and composites against the
actual destination in final packet order. Disjoint triangles share a screen copy;
overlapping triangles receive a new copy. Normal, Add, Multiply and Screen are
supported in both modes.

Forward+ and Mobile Gamma rendering require an **opaque viewport**. Their screen
copies do not expose the destination alpha needed for an exact transparent Gamma
composite. An unsupported transparent Gamma combination reports an error rather
than silently drawing a different result. Use **Linear** for transparent HDR
viewports, or **Compatibility** for transparent Gamma output. Compatibility's
destination copy includes alpha and supports both modes.

Imported Cane scenes use generated pixel-preserving Texture2D resources. Cane
reads source image bytes during import and saves the original pixels as ordinary
Godot resources under `.godot/imported`; they are dependencies of the animation
and are included by export. General PNG import options such as Fix Alpha Border,
extra premultiplication or lossy compression cannot modify these textures. The
importer refreshes them when the source image changes. Runtime loading of these
resources works from exported PCKs without the original PNG files.

For code-only `load_files` usage, import the Cane CANEB (or JSON through
**Tools → Import Cane animation**) first so its generated textures exist. If a
project intentionally bypasses Cane import, its ordinary texture imports must
preserve source pixels: disable Fix Alpha Border and Premult Alpha, use lossless
compression and no size reduction. Absolute file loading remains available for
external runtime resources and does not use Godot's editor import pipeline.
