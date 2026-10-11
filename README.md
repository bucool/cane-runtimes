# Cane Runtimes

**English** | [简体中文](README.zh-CN.md)

Independent runtimes for loading, evaluating and rendering Cane skeletal animation.
Each language has its own renderer-neutral Core. Engine adapters consume its final
geometry, UVs, tint and draw order and own engine resources and rendering.

| Language | Runtime |
| --- | --- |
| TypeScript | [Core](cane-ts/cane-core/README.md), [PixiJS](cane-ts/cane-pixi-v8/README.md), [LayaAir 3.4.1](cane-ts/cane-layaair-3.4.1/README.md), [Cocos Creator 3.8.8](cane-ts/cane-cocos-3.8.8/README.md) |
| C# | [Core](cane-csharp/README.md), [Unity](cane-csharp/cane-unity/README.md) |
| C++ | [Core](cane-cpp/README.md), [native Godot GDExtension](cane-cpp/cane-godot/README.md), [Unreal Engine 5.8 source plugin](cane-cpp/cane-unreal/README.md) |

## Install and use

This first public version distributes source code. Clone
[bucool/cane-runtimes](https://github.com/bucool/cane-runtimes) and follow the
source integration guide for your engine. Prebuilt packages and example archives
are not included in this source release.

| Engine | Installation |
| --- | --- |
| Unity | Install the local Core and Unity UPM packages from their `package.json` files. [Guide](cane-csharp/cane-unity/docs/INSTALLATION.md) |
| Godot 4.7.2, Windows x64 | Build the native extension and copy its addon files into the project. C#/.NET is not required. [Guide](cane-cpp/cane-godot/docs/INSTALLATION.md) |
| Unreal Engine 5.8 | Build the C++ Core and install the source plugin. [Guide](cane-cpp/cane-unreal/docs/INSTALLATION.md) |
| PixiJS v8 | Build the TypeScript Core and PixiJS adapter. [Guide](cane-ts/cane-pixi-v8/README.md) |
| Cocos Creator 3.8.8 | Build the TypeScript Core and exact-version Cocos adapter. [Guide](cane-ts/cane-cocos-3.8.8/README.md) |
| LayaAir 3.4.1 | Build the TypeScript Core and exact-version LayaAir adapter. [Guide](cane-ts/cane-layaair-3.4.1/README.md) |

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
