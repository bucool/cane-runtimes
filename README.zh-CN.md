# Cane Runtimes

[English](README.md) | **简体中文**

用于加载、计算和渲染 Cane 骨骼动画的独立运行时。
每种语言都有独立实现、不依赖具体渲染引擎的 Core。引擎适配层使用 Core 输出的
最终几何数据、UV、颜色和绘制顺序，并负责引擎资源管理与渲染。

| 语言 | Runtime |
| --- | --- |
| TypeScript | [Core](cane-ts/cane-core/README.zh-CN.md)、[PixiJS](cane-ts/cane-pixi-v8/README.zh-CN.md)、[LayaAir 3.4.1](cane-ts/cane-layaair-3.4.1/README.zh-CN.md)、[Cocos Creator 3.8.8](cane-ts/cane-cocos-3.8.8/README.zh-CN.md) |
| C# v2 | [Core](cane-csharp/cane-core/README.md)、[Unity](cane-csharp/cane-unity/README.md)、[可选适配包](cane-csharp/README.md) |
| C++ | [Core](cane-cpp/README.zh-CN.md)、[Godot 原生 GDExtension](cane-cpp/cane-godot/README.zh-CN.md)、[Unreal Engine 5.8 源码插件](cane-cpp/cane-unreal/README.zh-CN.md) |

## 安装与使用

原生 C# Core 和 Unity v2 已提供 pre-1.0 源码包，另有 URP、UI Toolkit 和
Addressables 可选集成。请参阅 [C# 系列指南](cane-csharp/README.md)及
[性能对比说明](cane-csharp/docs/PERFORMANCE.md)。
下方历史 v0.1.0 下载不包含本次新版 C#/Unity。

首个 Runtime 预览版已提供 [GitHub Release 下载](https://github.com/bucool/cane-runtimes/releases/tag/v0.1.0-preview.1)，
包含 Runtime 安装包、浏览器构建和 Windows Godot 原生扩展。
具体文件选择与 SHA-256 校验方式请见[下载指南](docs/DOWNLOADS.md)。

| 引擎 | 安装方式 |
| --- | --- |
| Unity v2 | 从磁盘依次安装当前 Core 和 Unity 源码包。[安装说明](cane-csharp/README.md) |
| Godot 4.7.2、Windows x64 | 将 addon ZIP 解压到工程，无需 C# 或 .NET。[安装说明](cane-cpp/cane-godot/docs/INSTALLATION.md) |
| Unreal Engine 5.8 | 安装附带 Win64 Core SDK 的插件源码 ZIP，然后编译 C++ 工程的 Editor 目标。[安装说明](cane-cpp/cane-unreal/docs/INSTALLATION.md) |
| PixiJS v8 | 安装 Core 与 PixiJS npm 压缩包，或使用浏览器构建。[使用指南](cane-ts/cane-pixi-v8/README.zh-CN.md) |
| Cocos Creator 3.8.8 | 将扩展 ZIP 解压到工程的 `extensions` 目录。[安装说明](cane-ts/cane-cocos-3.8.8/docs/EDITOR_WORKFLOW.zh-CN.md) |
| LayaAir 3.4.1 | 使用浏览器构建，或把 IDE 资源 ZIP 中的文件复制到工程。[安装说明](cane-ts/cane-layaair-3.4.1/docs/EDITOR_WORKFLOW.zh-CN.md) |

发行包仅包含 Runtime，不包含引擎 SDK、示例工程、Cane 编辑器或 Cane CLI。
也可以按下方说明从源码构建。

构建所需环境和命令请见[源码构建说明](docs/BUILDING.md)和
[API 指南](docs/README.md)。构建所需的引擎 SDK 作为外部依赖另行准备。

## 规范

[格式与算法规范](specification/README.md)定义了 Runtime JSON / CANEB 1.1、Atlas 1.0
以及 Runtime API 1.0–1.13。`spec-lock.json` 标识目标规范与参考测试套件的版本，
不代表测试通过报告。另请参阅[架构说明](docs/ARCHITECTURE.md)和[兼容性说明](docs/COMPATIBILITY.md)。

本公开源码仓库包含 Runtime 实现、必要的构建文件、第三方许可证声明和公开文档。
开发测试、性能基准、示例、本地工具、运行报告及进度记录不纳入公开仓库。
该仓库独立于 Cane/Coolbones 编辑器及其 Cargo 构建体系。
本仓库不包含 Cane 编辑器和 Cane CLI 的源码。
