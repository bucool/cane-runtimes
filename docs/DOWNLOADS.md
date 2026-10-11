# Runtime downloads / Runtime 下载

[Download v0.1.0-preview.1 / 下载首个预览版](https://github.com/bucool/cane-runtimes/releases/tag/v0.1.0-preview.1)

All assets belong to the same Runtime source snapshot and include Runtime API 1.13
mesh placement and Gamma/Linear composition support. Package-manager versions
remain as declared in each package; the release tag and SHA-256 identify this build.
This is a preview, not a certification of every engine/backend combination.

所有文件来自同一份 Runtime 源码，包含 API 1.13 网格整体位置修复和 Gamma/Linear 混合支持。
各包保留自身声明的版本，具体构建由 Release 标签和 SHA-256 标识。

| Target / 目标 | Download / 文件 | Install / 安装 |
| --- | --- | --- |
| TypeScript Core | `cane-runtime-core-0.1.0.tgz` | Local npm tarball / 本地 npm 包 |
| PixiJS 8.18.1 | `cane-runtime-pixi-v8-0.1.0.tgz` and Core; `cane-pixi-v8-browser-0.1.0-preview.1.zip` | npm packages, or load PixiJS then the IIFE / 安装 npm 包，或先加载 PixiJS 再加载 IIFE |
| LayaAir 3.4.1 browser | `cane-runtime-layaair-3.4.1-0.1.0.tgz` and Core; `cane-layaair-3.4.1-browser-0.1.0-preview.1.zip` | npm packages, or Core-inclusive ESM/IIFE / npm 包或内嵌 Core 的 ESM/IIFE |
| LayaAir 3.4.1 IDE | `cane-layaair-3.4.1-ide-assets-0.1.0-preview.1.zip` | Copy `assets/Cane` into the project / 复制到工程 |
| Cocos Creator 3.8.8 | `cane-cocos-creator-3.8.8-0.1.0-preview.1.zip` | Extract `cane-runtime` into `extensions` / 解压至扩展目录 |
| Cocos module API | `cane-runtime-cocos-3.8.8-0.1.0.tgz` and Core | Alternative source-module integration; choose either this or the extension / 与扩展二选一 |
| C# Core | `Cane.Core.0.1.0-alpha.5.nupkg` | Local NuGet source; targets .NET Standard 2.1 / 本地 NuGet 源 |
| Unity 6000.3+ | `com.cane.runtime.core-0.1.0-alpha.5.tgz` and `com.cane.runtime.unity-0.1.0-alpha.5.tgz` | Package Manager: install from tarball, Core first / 先装 Core，再装 Unity |
| C++ Core | `cane-cpp-source-0.1.0-preview.1.zip` | CMake source build, C++17 / 源码构建 |
| Godot 4.7.2 Win64 | `cane-godot-4.7.2-windows-x86_64-addon-0.1.0-preview.1.zip` | Extract `addons/cane` at project root; enable plugin / 解压并启用插件 |
| Unreal Engine 5.8 Win64 | `cane-unreal-5.8-win64-plugin-0.1.0-preview.1.zip` | Copy `CaneUnreal` into `Plugins`, compile the C++ project / 复制插件并编译工程 |

For npm adapters, install the Core tarball and adapter tarball in one command,
for example `npm install ./cane-runtime-core-0.1.0.tgz ./cane-runtime-pixi-v8-0.1.0.tgz`.
Supply the documented matching engine separately. Browser bundles contain Core,
but do not contain the engine. The Unreal ZIP contains Core headers and a Release
Win64 `/MD` static library; Unreal modules still compile in your own UE project.

The archives contain Runtime code and host-engine integration only. They do not
include the Cane editor, Cane CLI, engine SDKs, sample projects or artwork.
`release-manifest.json` records the source commit, asset checksums and verification
scope. Verify downloads against `SHA256SUMS.txt`; on Windows use
`Get-FileHash <downloaded-file> -Algorithm SHA256`.

压缩包仅包含 Runtime 和宿主引擎集成，不包含 Cane 编辑器、Cane CLI、引擎 SDK、示例工程或素材。
`release-manifest.json` 记录对应源码提交、文件校验值及验证范围。
可用 `Get-FileHash <下载文件> -Algorithm SHA256` 与 `SHA256SUMS.txt` 对照。
