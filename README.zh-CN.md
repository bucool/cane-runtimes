# Cane Runtimes

[English](README.md) | **简体中文**

用于加载、计算和渲染 Cane 骨骼动画的独立运行时。
每种语言都有独立实现、不依赖具体渲染引擎的 Core。引擎适配层使用 Core 输出的
最终几何数据、UV、颜色和绘制顺序，并负责引擎资源管理与渲染。

| 语言 | Runtime |
| --- | --- |
| TypeScript | [Core](cane-ts/cane-core/README.zh-CN.md)、[PixiJS](cane-ts/cane-pixi-v8/README.zh-CN.md)、[LayaAir 3.4.1](cane-ts/cane-layaair-3.4.1/README.zh-CN.md)、[Cocos Creator 3.8.8](cane-ts/cane-cocos-3.8.8/README.zh-CN.md) |
| C# | [Core](cane-csharp/README.zh-CN.md)、[Unity](cane-csharp/cane-unity/README.zh-CN.md) |
| C++ | [Core](cane-cpp/README.zh-CN.md)、[Godot 原生 GDExtension](cane-cpp/cane-godot/README.zh-CN.md)、[Unreal Engine 5.8 源码插件](cane-cpp/cane-unreal/README.zh-CN.md) |

## 安装与使用

首版以源码形式发布。克隆
[bucool/cane-runtimes](https://github.com/bucool/cane-runtimes)，
按照对应引擎的说明集成。本次源码发布不包含预编译安装包和示例压缩包。

| 引擎 | 安装方式 |
| --- | --- |
| Unity | 通过各自的 `package.json` 安装本地 Core 与 Unity UPM 包。[安装说明](cane-csharp/cane-unity/docs/INSTALLATION.md) |
| Godot 4.7.2、Windows x64 | 构建原生扩展，将插件文件复制到工程中；无需 C# 或 .NET。[安装说明](cane-cpp/cane-godot/docs/INSTALLATION.md) |
| Unreal Engine 5.8 | 构建 C++ Core 并安装源码插件。[安装说明](cane-cpp/cane-unreal/docs/INSTALLATION.md) |
| PixiJS v8 | 构建 TypeScript Core 和 PixiJS 适配器。[使用指南](cane-ts/cane-pixi-v8/README.zh-CN.md) |
| Cocos Creator 3.8.8 | 构建 TypeScript Core 和对应版本的 Cocos 适配器。[使用指南](cane-ts/cane-cocos-3.8.8/README.zh-CN.md) |
| LayaAir 3.4.1 | 构建 TypeScript Core 和对应版本的 LayaAir 适配器。[使用指南](cane-ts/cane-layaair-3.4.1/README.zh-CN.md) |

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
