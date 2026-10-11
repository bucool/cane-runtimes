# Source installation

The current target is UE 5.8, Win64, a C++ Unreal project, Shader Model 5 or higher,
the engine-supported MSVC toolchain, and CMake 3.24 or newer. The plugin rejects
other engine minor versions and platforms. UHT, Editor/Game compilation, nine
native checks in two fresh processes, and cooked-game GPU rendering have passed
on UE 5.8.3 with D3D12.

The Windows Editor target also requires the **.NET Framework 4.8 SDK** component
from Visual Studio Installer (the modern bundled .NET SDK is a separate product).
Unreal's SwarmInterface checks for this SDK before compiling any plugin code.

## Install the release plugin

The [Release](https://github.com/bucool/cane-runtimes/releases/tag/v0.1.0-preview.1) includes `cane-unreal-5.8-win64-plugin-0.1.0-preview.1.zip`.
Extract its `CaneUnreal` folder into `YourProject/Plugins`. The package includes
public Core headers and the matching Release `/MD` static library, so you can
skip Core staging below. It does not contain precompiled Unreal modules: regenerate
project files and build your C++ project's Editor target with UE 5.8, then restart
the editor. Runtime JSON/CANEB import and cooking follow the same steps below.

## Prepare the native Core

Use the same MSVC compiler and Windows SDK selected by UnrealBuildTool. The native
library uses `/MD`, C++17 and strict floating point. UE-facing source uses C++20
and enables exceptions so Core errors can become Blueprint error results.
Custom debug-CRT targets are not supported by this staged library.

Run in PowerShell with all output and temporary files on a non-C drive:

```powershell
$env:TEMP = 'E:\cane-runtimes\.tmp\unreal\temp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
cmake -S E:\cane-runtimes\cane-cpp\cane-unreal -B E:\cane-runtimes\.tmp\unreal\core -G "Visual Studio 17 2022" -A x64
cmake --build E:\cane-runtimes\.tmp\unreal\core --config Release --parallel
cmake --install E:\cane-runtimes\.tmp\unreal\core --config Release
```

This stages public headers and `cane-core.lib` into
`Source/ThirdParty/CaneCore/{Include,Lib/Win64}`. These generated files are ignored
by Git. Building Core alone does not build or validate the Unreal plugin.

When upgrading Core, stage the SDK again and rebuild the project's Editor and
Game targets. The build rules track the library's content fingerprint so the
runtime module and the editor importer both relink, even when public headers
are unchanged. Restart the editor after replacing native modules; copying only
`cane-core.lib` cannot update an already compiled or running plugin.

Copy `cane-unreal` (including the staged Core and `Shaders`) to
`YourProject/Plugins/CaneUnreal`. Keep the project, intermediate build products,
derived-data cache and packaging destination on a non-C drive. Preserve the Core's
[existing dependency notice](../../third_party/nlohmann/README.md) when redistributing it.

Enable **Cane Runtime** and **Procedural Mesh Component**. Regenerate project
files and build the project's Editor target with UE 5.8. Global shader modules
load at `PostConfigInit`; restart the editor after installing or changing shaders.
Do not rely on Live Coding for the first installation.

## Import and cook

1. Export native Cane Runtime JSON or CANEB, with its Atlas files and textures.
2. Keep dependencies under that export's root directory. Atlas page paths resolve
   relative to their Atlas file; direct images resolve relative to the root file.
3. In Content Browser, import the runtime `.json` or `.caneb`. Unrelated JSON is
   rejected. The importer decodes every texture, including unused Atlas pages,
   and lets Core validate observed dimensions before updating the asset.
4. PNG, JPEG and BMP are accepted. Imported raw textures have sRGB hardware decode,
   lossy compression, mip generation and streaming disabled; the shader owns
   color-space, alpha, wrap and min/mag sampling according to packet metadata.
5. Save the asset. Texture and material subobjects are referenced by the asset,
   so a game referencing it can cook them without reading the export directory.
6. Use **Reimport** to refresh the source and all dependencies. Failed preflight
   retains the previous asset. Reinitialize existing components/widgets after a
   successful reimport to create new players over the updated immutable data.

The importer initially fits the setup pose with padding. For UMG or scene
`IsolatedSurface` mode, set **Surface Origin**, **Surface Size** and **Resolution**
to cover the complete animation range. Framing stays fixed during playback.
The default `DirectScene` mode uses final triangles and the host camera instead;
these surface settings do not crop or scale that path.

Direct scene rendering currently targets deferred, non-MSAA, non-stereo views
with post-processing enabled. It hooks the UE 5.8 `BeforeDOF` pass. It does not
participate in mesh hit proxies, lighting, shadow maps or motion-vector generation;
use the World Outliner to select its actor. These integration limits and real
engine acceptance remain relevant even when native Core tests pass.

## Required engine acceptance

Before treating this source as production-ready, run UHT, Editor and packaged
Win64 builds, import/reimport failure checks, shader compilation, scene and UMG
visual tests, repeated destruction/reload, and a packaged game with source files
unavailable. Core conformance alone is not sufficient evidence for these checks.
