# AnimationPlayback 求值与事务发布

依赖 [姿态组合模型](TRACK_POSE_COMPOSITION_DESIGN_V2.md) 与
[通道投影](TRACK_CHANNEL_PROJECTION_V2.md)，由 `animation.track.pose-v2` 选择。

## 图与延迟求值

轨道按索引从低到高应用。每个 AnimationPlayback 是对同一个低层姿态 L 的操作：
独立计算 O(L) 和 I(L)，然后按进度 m 混合；不是把 I 再叠到已衰减的 O 上。
轨道队列、时间推进、事件收集沿用当前生命周期模块，此求值阶段只读取已确定的链。

- 从当前条目迭代走向前驱，m=1 时截断整个前驱分支。再逆序构造组合图，不递归。
- m=0 时只求值前驱，不采样当前条目的数值、曲线、附件或排序；无需其缺失掩码。
- 0<m<1 时两端均参与；空动画为恒等操作。`hold_previous_pose` 仅持有当前端缺失的字段，
  完成时释放。这里的缺失是该时刻和门槛下的通道缺失，不是静态属性集合。
- alpha=0 时条目是恒等操作，但仍可能声明某个字段；只提取时刻相关的存在掩码，
  用 setup 值作为不被求值的占位。不计算曲线或 canonical deform，不扩大 choice table。
  因而“X 有键但透明”与“没有 X 键”对 hold 的含义不同。
- 连续字段不受附件/排序门槛限制。当前条目的离散门槛开放；每个前驱自身的离散门槛
  只与其直接后继进度比较：附件/皮肤/序列使用 attachment_blend_threshold，排序使用
  draw_order_blend_threshold，严格 `<`。祖先门槛不再次乘到子链；组合进度单独决定贡献。
- metadata 掩码遵守分轴/成对通道、独立 alpha 覆盖、可选约束字段、Physics global 标记、
  before-first-key 及离散门槛规则。它不是数值校验器，格式/引用校验仍由既有层负责。

独立可手算检查：S=0，A=10、alpha=.25，B=30、alpha=.5，m=.5，
两端为 2.5 与 15，结果 8.75。添加任意 alpha=0 的低层轨道仍为 8.75。
S=0，A 的 X=10、Y=20，B 透明且仅有 X 键，m=.5、hold 开启：X=5，Y=20。
若 B 的 X 首键还未到达，则 X=10。完成后均回到低层。

## 分离准备与发布

从真实条目图生成已参与的样本，交给独立组合器，再将完整 typed channels 解码为
detached AnimationSample。覆盖骨骼八字段、槽位换图/RGB/alpha/dark presence、
Region、sequence、独立 deform owner、最终 draw order/skins 及五类约束的 27 字段。
RGB 在发布时按 round(clamp(channel,0,1)*255) 编码；颜色时间采样在投影前可能已发生量化。持有与没有 dark color 由独立布尔字段决定。

全部采样、投影、组合、解码、时钟检查和 reset 计划成功后才发布。失败必须保持实例
的姿态、时钟、Physics 历史、采样选项和采样侧的时钟标识完全不变。
已有 linked geometry、皮肤映射和世界求解边界不变。公开 Runtime API 调用保持相同的事务发布边界。

采样时钟标识由 primary animation id、range start/end、looping 构成，由采样器侧保存。
首次进入采样、标识改变、主时钟倒退均清除旧 Physics 历史；其他情况下保留已有
primary clock/delta 规则。非有限采样时刻或无法表示的 delta 返回错误而不发布。
同名同范围的条目重建仍受既有时钟生命周期规则约束，不另行建立条目身份协议。

## Physics reset 的明确政策

Reset 是状态事件，不是数值通道，也不根据动画名字搜索所有图节点。
只允许主轨道 0 当前条目触发；其 alpha*m 的精确 f64 乘积至少 .5，且时钟有前值。
高层同名动画和已退场前驱均不能代表主轨道重置模拟。

根据**最终组合皮肤**判断目标约束及其骨骼是否激活，通配符与指定 ID 使用同一活性条件。
计划中 ID 去重；只重置已存在的状态。

key 位于所选闭区间 [start,end] 才参与。非循环只检查一次偏移 t=key-start，
采用 (from,to]；循环检查 t+kD，D 是 AnimationPlayback 现行 f32 range duration，k>=0。
首尾键明确对应 0 和 D；中间键的偏移使用现行 f32 减法，与条目时域一致。
周期存在性复用已独立验证的整数区间内核，不逐圈枚举；该核不提供 reset 的政策。
首次采样/倒退/标识变化由清空历史处理，不另追放过去 reset。相同时刻不重复触发。

## 验证要求

使用自制骨架验证真实链的两端、部分 alpha、空动画、叠加轨道、中断链、hold 缺失、
before-first、直接后继门槛及零贡献错误曲线。检查颜色/附件/排序/变形/约束发布，
主轨道同名重置、最终皮肤活性、裁剪播放范围、周期/端点、时间倒退和失败原子性。
使用独立 Fraction 计算 reset 区间答案；验证循环区间和端点归属。
