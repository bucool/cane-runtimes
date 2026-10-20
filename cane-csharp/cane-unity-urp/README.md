# Cane for Universal Render Pipeline

Optional scene shaders for Unity 6000.3 / URP 17.3. Install this UPM package beside
`com.cane.core` and `com.cane.unity` in a URP project. Forward, Forward+ and 2D
rendering are validated on Windows x64 Mono/D3D11 in Gamma and Linear.
The base Cane package has no URP dependency.

## Choosing a shader

Select a serialized shader reference on `CaneAsset.SceneShader` (or the `.cane2`
importer's `Scene Shader` field):

| Shader | Renderer |
| --- | --- |
| `Cane/URP/Unlit` | Universal Renderer or 2D Renderer, without lighting |
| `Cane/URP/Lit` | Universal Renderer, 3D lighting |
| `Cane/URP/2D Lit` | 2D Renderer, Light2D lighting |

For resources created in code, pass the reference at construction:

```csharp
using var resources = new CaneResources(asset, textures, sceneShader: sceneShader);
player.Initialize(resources);
```

The choice applies to shared scene materials. Texture replacement, the exported
blend modes and SpriteMask settings keep using the resource material cache.
Canvas uses the base package's UI shader. Keep the shader in a serialized asset
or material so Unity includes it in the Player; `Shader.Find` alone does not
retain it. After changing a `CaneAsset` in code, call `Reload()` to invalidate its
shared resources. Existing users rebuild on their next advance.

## Lighting and materials

Enable `CaneRenderer.AddNormals` for lighting and shadow casting, and
`CaneRenderer.SolveTangents` for normal maps (it also enables normals). Unlit
rendering without shadows needs neither stream. Tangents are generated from
final deformed geometry and atlas UVs. Use
`SlotMaterials` for custom materials, with prepared textures from
`CaneResources.Textures`, matching `_Pma`, and the existing sampling settings.
These are scene materials, not Canvas materials.

`Cane/URP/Lit` uses URP's lighting functions in the Forward and Forward+ paths.
It provides normal strength, metallic/smoothness and emission maps, realtime
shadow casting/receiving, and fog. Opacity attenuates the full lit result,
including specular and emission. `_MetallicGlossMap` red and alpha multiply
`_Metallic` and `_Glossiness`. `_Cutoff` controls shadow/depth coverage.

`Cane/URP/2D Lit` uses the 2D Renderer's four light blend styles, `_MaskTex`, and a
normal pass with `_BumpMap`. Ordinary skeleton clipping stays in Core; compatible
scene objects bound through `CaneSlotObject` use the shared slot clipping shader.

Both lighting paths preserve Core's two-color tint. Scene materials default to
no depth writing; enable `_ZWrite` only when the chosen depth/ordering behavior
is appropriate. Native MeshRenderer controls govern shadows and probes. These
shaders do not provide a deferred G-buffer or an HDRP implementation.

All three shaders share the base package's optional hue, saturation, brightness,
secondary albedo, grayscale, fill and outline controls. `Cane/URP/Lit` also
provides a diffuse light ramp and view-dependent rim color. Set `_Effects` to
one to enable these controls; no shader keyword is needed. Their neutral defaults
preserve Unity's native lighting. The light ramp follows the mapped surface normal
and each realtime light, while the rim is added once per surface.
See [material effects](../cane-unity/README.md#material-effects)
for activation, width units, atlas bounds and mesh-padding requirements.
The effects change the shaded surface; they leave shadow and depth coverage
unchanged. The 2D normal pass includes outline coverage for Light2D.

See the [base Unity guide](../cane-unity/README.md) for animation, scheduling,
skins, resource ownership, separators and Canvas integration.
