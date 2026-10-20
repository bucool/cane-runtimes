# Cane Runtimes

**English** | [简体中文](README.zh-CN.md)

Independent runtimes for loading, evaluating and rendering Cane skeletal animation.
Each language has its own renderer-neutral Core. Engine adapters consume its final
geometry, UVs, tint and draw order and own engine resources and rendering.

| Language | Runtime |
| --- | --- |
| TypeScript | [Core](cane-ts/cane-core/README.md), [PixiJS](cane-ts/cane-pixi-v8/README.md), [LayaAir 3.4.1](cane-ts/cane-layaair-3.4.1/README.md), [Cocos Creator 3.8.8](cane-ts/cane-cocos-3.8.8/README.md) |
| C# v2 | [Core](cane-csharp/cane-core/README.md), [Unity](cane-csharp/cane-unity/README.md), [optional adapters](cane-csharp/README.md) |
| C++ | [Core](cane-cpp/README.md), [native Godot GDExtension](cane-cpp/cane-godot/README.md), [Unreal Engine 5.8 source plugin](cane-cpp/cane-unreal/README.md) |

## Install and use

The native C# Core and Unity v2 packages are available as pre-1.0 source, with
optional URP, UI Toolkit and Addressables integrations. See the
[C# family guide](cane-csharp/README.md) and
[measured performance profile](cane-csharp/docs/PERFORMANCE.md).
The historical v0.1.0 downloads below do not contain these replacement packages.

Download the first Runtime preview from [GitHub Releases](https://github.com/bucool/cane-runtimes/releases/tag/v0.1.0-preview.1).
It includes installable Runtime packages, browser bundles and a Windows Godot
extension built from this source. See the [download guide](docs/DOWNLOADS.md)
for the exact asset to choose and SHA-256 verification.

| Engine | Installation |
| --- | --- |
| Unity v2 | Install the current Core and Unity source packages from disk, in that order. [Guide](cane-csharp/README.md) |
| Godot 4.7.2, Windows x64 | Extract the addon ZIP into your project. C#/.NET is not required. [Guide](cane-cpp/cane-godot/docs/INSTALLATION.md) |
| Unreal Engine 5.8 | Install the source plugin ZIP with its bundled Win64 Core SDK, then compile your C++ project's Editor target. [Guide](cane-cpp/cane-unreal/docs/INSTALLATION.md) |
| PixiJS v8 | Install the Core and PixiJS npm tarballs, or use the browser bundle. [Guide](cane-ts/cane-pixi-v8/README.md) |
| Cocos Creator 3.8.8 | Extract the extension ZIP into the project's `extensions` directory. [Guide](cane-ts/cane-cocos-3.8.8/docs/EDITOR_WORKFLOW.md) |
| LayaAir 3.4.1 | Use the browser bundle or copy the IDE assets ZIP into your project. [Guide](cane-ts/cane-layaair-3.4.1/docs/EDITOR_WORKFLOW.md) |

The release contains Runtime packages only. Engine SDKs, sample projects,
Cane editor and Cane CLI are not included. Source builds remain available.

For build prerequisites and commands, see
[building from source](docs/BUILDING.md) and the [API guides](docs/README.md).
Engine SDKs are external build dependencies.

## Specification

[Format and algorithm specifications](specification/README.md) define Runtime
JSON/CANEB 1.1, Atlas 1.0 and Runtime API 1.0–1.13. `spec-lock.json` identifies the target
specification and reference-suite version; it is not a test report.
See [architecture](docs/ARCHITECTURE.md) and [compatibility](docs/COMPATIBILITY.md).

This source repository contains Runtime implementations, required build inputs,
third-party license notices and public documentation. Development tests,
benchmarks, examples, local tools, run reports and progress records are excluded.
It is independent from the Cane/Coolbones editor and its Cargo build graph.
Cane editor and Cane CLI source are not included in this repository.
