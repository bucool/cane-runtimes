# 在 Unreal 5.8 中使用 Cane

当前插件面向 Windows x64、UE 5.8 的 C++ 工程。先按[源码安装说明](INSTALLATION.md)
准备 C++ Core，将插件放入工程的 `Plugins/CaneUnreal`，编译 Editor 目标后重启 UE。
工程、中间产物、缓存和打包目录应放在非 C 盘。

## 导入角色

1. 从 Cane 导出 `.caneb`，同时保留对应的 Atlas JSON 和贴图，保持导出目录结构。
2. 在 UE 内容浏览器中导入 `.caneb`。插件会创建 `Cane Skeleton` 资源，保存数据、
   贴图与显示材质。原生 Cane Runtime JSON 也能导入。
3. 保存资源。修改源文件后，在内容浏览器中右键选择重新导入，再重新初始化使用它的实例。

Spineboy 导出的默认播放动画可填 `run`，也可选择 `idle`、`walk`、`jump` 等实际动画 ID。
资源面板中的 `Animations` 列表是该资源可用动画的依据。

## 放进场景

添加 `CaneActor`，在它的 `Cane` 组件中设置 `Asset` 和 `InitialAnimation`。
勾选 `bPreviewInEditor` 可以在编辑器中预览；游戏运行后会自动推进播放。

角色位于 Unreal 的 X/Z 平面，Z 向上。让相机从负 Y 方向朝向角色，
使用 Actor 变换调整位置，用 `UnitsPerCaneUnit` 调整尺寸。默认直接渲染模式
支持与场景背景混合；通过世界大纲选择 Actor。

蓝图中读取组件的 `Player`，调用 `Play` 切换动画，调用 `Queue` 排队，
通过 `MixSeconds` 设置过渡。`bPaused` 控制暂停，`PlaybackRate` 控制速度。
组件已经自动推进时间，不要再每帧调用 `Advance`，否则播放会加速。

## 放进 UI

在 Widget Blueprint 中添加 `Cane Animation`，设置 `Asset` 和 `InitialAnimation`。
每个控件有独立的 Player，可分别播放。默认 `DirectSlate` 按控件所在层绘制，
支持布局变换、父级裁剪和透明度；普通 Slate 目标保留 `TargetColorSpace = Auto`。

控件使用资源的 `Surface Origin` 和 `Surface Size` 作为取景范围。导入时按初始姿势
计算，动作幅度大时应扩大范围。`IsolatedSurface` 模式还受 `Resolution` 影响。

`SetSkeleton` 传入空值会清空场景组件或 UI 控件；换入有效资源会创建新播放器。
播放或渲染失败时查看 `LastError`，播放器也提供 `OnError` 事件。

## 事件和挂点

绑定 `Player.OnAnimationEvent` 接收播放和自定义事件。声音 ID 只作为事件数据返回，
实际播放音频由游戏处理。

添加 `CaneBoneAnchorComponent`，设置 `Source`、`BoneId` 与 `LocalOffset`，
即可跟随骨骼上的位置。它保留镜像和非均匀缩放后的完整坐标轴；骨骼含剪切时，
不要把返回的坐标轴简单近似成普通旋转和缩放。

打包时让地图或蓝图引用 Cane 资源，或将对应内容目录加入打包设置。
贴图和材质随资源一起 Cook，成品运行不应依赖外部导出目录。
渲染平台、透明物体排序、Retainer 和 HDR 的具体边界见[渲染说明](RENDERING.md)。
