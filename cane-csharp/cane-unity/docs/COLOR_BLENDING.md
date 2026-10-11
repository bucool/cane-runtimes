# Color blending

The camera pass defaults to **Gamma**. Select **Linear** for linear-light blending:

```csharp
camera.GetComponent<CaneCameraRenderer>().BlendColorSpace = CaneBlendColorSpace.Linear;
```

The same `BlendColorSpace` setting is available on the URP `CaneRendererFeature`,
HDRP `CaneCustomPass`, and programmatic `CaneSceneRenderer`. It applies to the
ordered Cane pass for that camera and can change without reloading animation data.
Keep Unity's project color space **Linear** in both modes. This renderer option
does not change the project's lighting or other Unity materials.

Gamma uses a floating-point working surface containing the existing destination
converted to Gamma. Texture samples and tint are converted to the selected space
before interpolation and blending. Normal, Add, Multiply and Screen use their
usual hardware blend factors, including overlapping triangles. The completed
surface is converted back to the camera's linear target. Slot content that uses
ordinary Unity materials is drawn against a temporarily restored linear surface.
Linear uses the original linear target directly.
Built-in and URP request a readable camera intermediate automatically for Gamma;
target-aware custom hosts must provide a readable linear color target.

Gamma currently supports a single 2D camera view, including HDRP's single-slice
texture arrays and MSAA targets. Multi-view/XR texture arrays and cubemap targets
are rejected explicitly; use Linear for those targets.

Atlas `colorSpace`/`alphaMode` declarations remain unchanged. A canonical
premultiplied texel is decoded and unpremultiplied in linear light before being
converted and premultiplied in Gamma; applying Gamma directly to premultiplied
RGB would change edge opacity and halos.

For transparent target transfers, coverage is `clamp(alpha, 0, 1)` and RGB is
unpremultiplied, transferred, then premultiplied. Additive alpha above one does
not divide or dim opaque radiance. The alpha blend equations themselves do not
change. Camera passes infer a transparent target from a Solid Color clear whose
alpha is below one; custom hosts can pass `transparentTarget` explicitly to the
target-aware `CaneSceneRenderer.Record` overload.

Low-level `CaneSkeleton.Record`, `CaneMeshProjection.Record`, and the generic
draw-only scene-recorder overload do not own or convert a render target. They
retain their linear target contract; the generic scene recorder requires an
explicit `BlendColorSpace = CaneBlendColorSpace.Linear`. Custom hosts wanting Gamma should use the
target-aware scene-recorder overload, which also owns destination conversion.
Do not just set a shader flag in an otherwise linear render pass.
