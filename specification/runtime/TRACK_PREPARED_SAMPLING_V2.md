# 静态采样数据与 Slider 通路

本契约补充 [条目求值](TRACK_ENTRY_EVALUATION_V2.md)。

## 不可变 schema 与每次操作的数据

CaneData 的文件及解析结构在构造后不可变，因此可按**数据对象**保存一次性 schema，
不以路径、名字、地址散列或进程全局状态推断内容身份。

Schema 持有排序后的通道键/索引、slot token 映射、固定 choice 与 setup pose。
首次使用时在 CaneData 内的 OnceLock 中建立 Result<Arc<Schema>, Error>；同一不可变
数据的克隆可以共享已建 schema。新数据对象独立建立，即使工程和骨骼名字相同。
失败记录只属于该不可变对象，不写用户文件，不影响其他对象。

每次操作只保留临时 choice、输入 sample/图和输出。choice token 先引用 schema 的固定
区间，再引用本次临时区间；失败截断本次 choice，不能修改或扩大共享 schema。
活跃皮肤是实例数据，以初始姿态的一个离散覆盖项传入组合器，不能写入共享 setup。
因此不同实例可以共享 schema 同时保持各自的皮肤。对 Additive 的 setup 参照保持不变。
初始覆盖必须验证索引、重复项、值类型/布局；验证计入组合预算，失败无输出。

先用自制 8/64/256 骨骼和常量轨道测量现有候选 CPU，预热后报告中位数/P95；
随后用相同输入和命令测量。数据准备不计入每帧计时，第一次冷准备另记。
dev CPU 测量不等于 release、真实项目、渲染或画布验收，不设不稳定的墙钟断言。

## Slider 是现有姿态上的一次操作

保留既有 Slider 的激活检查、源骨骼局部/世界取值、时间映射、循环/夹取、约束顺序
和诊断计算。目标动画的应用路径复用 typed channel
采样和独立组合数学，不能另外复制一套权威世界求解。

底层姿态来自当前已解约束的 solved_poses 及当前槽位/附件/skin/排序/约束状态；
setup 仍来自静态 schema。目标动画没有该通道时保留当前值。

- Replace：L + a(X-L)；Additive：L + a(X-S)。角度使用既定最短差规则。
- Slider 的 a 允许任何有限值，保留负数和大于一的外推；AnimationPlayback 的 alpha 仍为 [0,1]。
  用单独的 layer 入口表达该区别，不放宽普通轨道/transition 权重验证。
- 有界字段在完整操作后夹取，离散字段仍按 a>=.5 选择；a=0 保持底层且跳过目标采样。
- 当前 channels -> owned underlay -> 单层操作 -> detached sample -> 发布目标。
  失败保持传入的目标集合；世界求解依靠既有 clone/commit 事务保持整个实例一致。
- Slider 目标动画的 reset 不是值通道，不因此重置主轨道 Physics 时钟。
- Slider 的错误通过 Runtime API 的结构化错误通路向调用方报告。

测试必须经过实际 Slider 约束驱动：手算 Replace/Additive/负数/外推、之前的约束作为
低层、缺失通道、离散 tint/附件和 before-first、目标曲线失败的整实例事务。
单独的反向投影 roundtrip 不足以证明 Slider 已接入。

## 共享预算

Schema 复用减少重复准备；工作准入不因冷热缓存改变。准备、采样、投影、组合、发布与
所有 Slider 共用 [采样预算](TRACK_SAMPLING_WORK_V2.md)。失败保留实例与调用方预算。
