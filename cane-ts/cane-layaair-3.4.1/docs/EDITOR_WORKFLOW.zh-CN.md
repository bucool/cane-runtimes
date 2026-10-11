# LayaAir IDE 使用流程

[English](EDITOR_WORKFLOW.md) | **简体中文**

使用 **LayaAir IDE 3.4.1**。从 [GitHub Release](https://github.com/bucool/cane-runtimes/releases/tag/v0.1.0-preview.1)
下载 IDE 资源 ZIP。Core 和渲染 Runtime 已编译为 JavaScript，附带配套类型声明。
组件及 IDE 集成脚本由 IDE 自动编译。

## 安装

1. 关闭目标工程，把 ZIP 中的 `assets/Cane` 目录解压到工程中，保留此路径和全部 `.meta` 文件。
2. 使用 LayaAir IDE 3.4.1 重新打开工程，等待脚本编译完成。
3. 按下方步骤导入自己的 Runtime 导出文件，选中生成的节点，设置动画、皮肤、循环、速度和自动播放。
4. 勾选 **编辑器预览 / Preview** 在场景视图中播放，或点击 **预览运行** 运行游戏。

每个工程只安装一份。此 ZIP 通过复制文件安装，不使用 `.layapkg` 导入或包管理器。
不要同时加载第二份浏览器 Runtime。发行包不包含示例场景或素材。
源码集成请见[构建说明](../../../docs/BUILDING.md)。

## 导入自己的动画

从 Cane 导出 **Runtime JSON 或 CANEB**，连同 Atlas JSON、图片一起放入
`assets/resources` 下，保留导出时的相对目录结构。Cane 工程文件和 Spine JSON 不能直接作为 Runtime 数据导入。

导入器只识别 Cane Runtime JSON，不会接管其它普通 JSON。展开源资源，可看到生成的
`.caneasset` 骨骼数据。把源资源或生成的数据拖入场景视图、层级面板，就会自动创建带有
Cane Skeleton 组件的 Sprite。也可为 Sprite 添加 **Cane → Cane Skeleton** 组件，再为“数据”字段选择生成的骨骼数据。

选择动画、皮肤，设置非负播放速度，然后保存场景或预制体。“数据”字段使用引擎资源引用，
关闭并重新打开工程后仍会恢复配置。编辑器预览不会被带入游戏；游戏播放由“自动播放”单独控制。

“皮肤”留空时使用导出数据中的 `default` 皮肤（如果存在）。Mix-and-Match 等换装角色的
默认皮肤可能本来就是空的；应选择基础皮肤，再按需通过 Runtime API 组合其它皮肤。

## 依赖与重导入

请把引擎生成的 `.meta` 文件与资源一起纳入版本管理。通过 IDE 重命名或移动文件，才能保留其资源 ID。
导入器跟踪骨骼、图集和图片依赖，并在重导入后刷新已加载的预览。
生成的原始纹理子资源保留导出图片的像素，避免引擎自动合图、裁边改变 Cane 的 UV 或透明度含义。

导入失败时会指出源文件以及缺失、无效的依赖。恢复文件，必要时重新导入源资源。
无效数据不会悄悄沿用上一次成功导入的旧画面。生成的 `.caneasset`、`.canetex` 由插件维护，不要手动修改。

## 脚本控制

```ts
import { CaneSkeleton } from "../assets/Cane/CaneSkeleton";

// 根据当前脚本位置调整相对导入路径。
const skeleton = node.getComponent(CaneSkeleton);
await skeleton.ready;
if (!skeleton.runtime) throw new Error(skeleton.status);
skeleton.animation = "run";
skeleton.loop = true;
skeleton.speed = 1;
skeleton.playOnAwake = true;
const unsubscribe = skeleton.runtime.onEvent(event => console.log(event));
// 监听者销毁时调用 unsubscribe。
```

节点发出 `cane-ready`、`cane-error` 事件。`ready` 在当前加载结束后完成，
通过 `status`、`runtime` 判断加载是否成功。切换“数据”会安全取消之前的加载。
多个实例共享只读数据和纹理，各自拥有独立播放器；销毁一个节点不会释放其它实例仍在使用的资源。

## 构建与支持范围

通过 **文件 → 构建发布 → Web** 构建，使用工程配置的 WebGL 或 WebGPU 渲染后端。
骨骼数据和纹理依赖通过引擎资源链接进入构建。请用 HTTP 服务打开构建结果；
WebGPU 需要支持它的浏览器以及安全上下文，localhost 可用于本地验证。
TypeScript Core 面向 ES2022，使用 BigInt，需要现代浏览器；把编译目标设为 ES2015 不会使 Runtime 兼容旧浏览器。

本包支持精确 3.4.1 版本的 WebGL、WebGPU；不将 Native、NoRender、Canvas2D 或小游戏平台列为已验收目标。
官方无渲染 CLI 可以完成导入、保存和构建，但不能显示预览。
渲染器、设备丢失限制见 [Runtime 说明](../README.zh-CN.md)。
编辑器和游戏共用 Core 播放器，预览重绘不会额外求值一次动画。
