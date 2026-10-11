# Cane Unreal Engine 适配器

[English](README.md) | **简体中文**

面向 **Unreal Engine 5.8 / Windows x64** 的源码插件，复用独立的
[C++ Core](../README.zh-CN.md)。格式目标为 Runtime JSON/CANEB 1.1、Atlas 1.0、
Runtime API 1.13；不依赖 Rust、C ABI 或 Cane 编辑器。

**当前为源码集成版本。已通过 UE 5.8.3 / Win64 D3D12 的 UHT、Editor/Game 编译，
以及两个独立引擎进程中的全部 9 项测试。** 覆盖导入、重新导入、播放事件、生命周期、
四种混合、场景背景/遮挡/曝光、骨骼挂点，以及 UMG 变换、裁剪和 Retainer 缓存。
Win64 打包也已通过，打包程序在导入源文件不可用时成功加载并渲染了 Spineboy。

- 资源导入：原生 JSON/CANEB、图集、贴图、重新导入，以及打包所需资源。
- 场景：`ACaneActor` 和 `UCaneComponent`。
- 蓝图：`UCanePlayer` 提供播放、队列、轨道、混合、事件、皮肤、附件和骨骼查询。
- UI：`UCaneWidget`，可放入 UMG。
- 骨骼挂点：`UCaneBoneAnchorComponent` 用完整仿射矩阵计算挂点位置。

场景组件、UMG 和独立纹理均提供 `BlendColorSpace`，默认 `Gamma`，可切换为
`Linear`。筛选、颜色着色和四种混合模式在所选空间中计算。蓝图或 C++ 调用
`SetBlendColorSpace` 可在暂停时直接切换，不重置动画。独立纹理对 UE 材质始终
输出线性预乘颜色，不需要在切换时修改材质。

动画、约束、权重、裁剪、最终顶点、UV、颜色和绘制顺序都由 Core 计算。
插件负责 UE 的导入、资源管理和显示，不再实现一套骨骼计算。

场景组件默认直接绘制最终三角形，在景深之前与场景颜色混合，并处理深度遮挡
和预曝光。UMG 默认直接使用 Slate 绘制，背景混合、裁剪和 Retainer 检查
已经通过。另保留独立纹理模式；该模式下乘法、滤色只作用于纹理内部，不能直接
影响外部场景或其他控件。原生 HDR UI 平面、立体视图和多重采样场景深度不在
当前支持范围内，多世界和多视口的复杂组合尚未全面验证。
挂点提供准确的位置和完整坐标轴，不将剪切骨骼近似成普通旋转缩放。

详细步骤见[中文快速上手](docs/QUICKSTART.zh-CN.md)、[安装](docs/INSTALLATION.md)、[API](docs/API.md)和
[渲染范围](docs/RENDERING.md)。
