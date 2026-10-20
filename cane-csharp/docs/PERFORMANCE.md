# Unity performance profile

Measured on 2026-10-20 with Unity **6000.3.23f1**, Windows x64, Mono release
Players, Low managed stripping, D3D11 and an NVIDIA RTX 3060 Laptop GPU.
The scene profile uses URP 17.3 Forward, Gamma, Spineboy's `walk` animation,
original JSON/binary exports and the same unchanged PNG.

The official comparison uses spine-csharp 4.3.39, spine-unity 4.3.98 and URP
shaders 4.3.25 from commit `a4a1642b8394ec8d1a7ec7afceea5214bb2af3c4`.
Both sides retain the installed game-integration APIs and optional UI/streaming
modules. These measurements describe this profile; other assets, backends and
deployment targets require their own measurements.

## Scene rendering

Values are medians of fresh-process trials, in milliseconds.

| Format | Characters | Cane completed frame | Spine completed frame | Cane cold first frame | Spine cold first frame |
| --- | ---: | ---: | ---: | ---: | ---: |
| Binary | 1 | 0.356 | 0.360 | 69.16 | 82.20 |
| JSON | 1 | 0.352 | 0.356 | 78.05 | 101.04 |
| Binary | 64 | 4.143 | 4.491 | 90.27 | 102.36 |
| JSON | 64 | 4.125 | 4.481 | 98.12 | 118.89 |

Single-character cost is effectively tied at this scale. With 64 characters,
Cane's completed-frame median is about **8% lower**. All four cold-load
medians are lower. Both implementations allocate **0 managed bytes** in the
measured steady animation/render loop.

Each completed frame includes animation/constraint evaluation, geometry, Unity
mesh upload, an explicit 800×800 render and a synchronous one-pixel GPU readback.
It measures completed-frame latency, not pipelined game FPS. The common light,
flat normal, texture and camera inputs are identical; native lighting shaders
retain their own equations.

For steady timing, both implementations reside in one process and alternate
render order. Each of four fresh trials measures 1,800 frames after 180 warm-up
frames. Separate Cane/Cane trials quantify measurement variation.

Cold timing uses six fresh processes per configuration with alternating runtime
order, after one empty engine frame. It includes texture loading, parsing,
resource/instance creation and the first completed character render. Runtime
data, instances and character materials are not prewarmed; OS and driver caches
remain warm. Remote Addressables transfer time is outside this profile.
All trials are retained. Earlier slower measurements remain in the local
validation archive; the current figures are not guarantees for every launch.

## UI Toolkit

The native 800×800 panel profile uses the same resources and release settings.
Each fresh process measures 1,200 completed frames after at least 180 warm-up
frames and two seconds of warm-up. Four trials per configuration alternate
runtime order. The frame interval includes manual animation update, native UI
mesh generation/layout/rendering and synchronous one-pixel GPU completion.
Every requested character is checked for visible rendered pixels.

| Format | Characters | Cane completed frame | Spine completed frame | Cane cold panel | Spine cold panel |
| --- | ---: | ---: | ---: | ---: | ---: |
| Binary | 1 | 0.171 | 0.203 | 88.12 | 92.26 |
| JSON | 1 | 0.174 | 0.203 | 98.16 | 113.71 |
| Binary | 64 | 3.135 | 4.053 | 105.12 | 115.44 |
| JSON | 64 | 3.106 | 4.061 | 118.94 | 137.68 |

Values are trial medians in milliseconds. The 64-character frame median is
about **23% lower**. Both implementations allocate 0 managed bytes in the
measured steady loop. Cold timing starts after an empty panel frame and ends
at the first populated panel frame; OS/driver caches remain warm.

## Packaged size

| Actual Player increment over the common engine baseline | Bytes |
| --- | ---: |
| Cane | 4,558,662 |
| Spine | 5,312,323 |

Cane's increment is **14.19% smaller**. The common empty Player is 77,243,879 B.
The comparison counts shipped files, including Core, Unity adapters, required
dependencies, native libraries and retained shaders. Harness metadata and
Unity's non-shipping Burst debug directory are excluded from both sides.

All installed API assemblies are retained with matching Low stripping settings.
The common baseline has the same Unity/URP/game modules; additional Addressables
and UI dependencies are charged to each implementation. The material set covers
unlit, normal/two-color, 3D and 2D lighting, emission/gloss, grayscale/fill/outline,
HSV, secondary albedo, diffuse ramps and rim controls. It retains the listed
capabilities rather than every possible shader-keyword permutation.

## Repeating a comparison

Use the same original resources, engine version, rendering API, color space,
camera/light inputs, build settings and retained feature set for both Players.
Keep import, compilation and other heavy work out of timing runs. Retain package
hashes, resolved dependencies, Player-file hashes, per-frame samples and all
fresh-process results. Compare complete frames and first visible frames rather
than substituting a parser or animation-only microbenchmark.
