# CPU 分派与计算优化范围

[文档导航](../../README.md) · [仿真接口](../../interfaces/simulation.md) · [音频接口](../../interfaces/audio-persistence.md)

## 计算边界

优化覆盖定点数值原语、波形事件合成、压力／Pleth 递推、氧合积分、测量统计与历史读取、
波形封包校验，以及预录 PCM 转换、混合和限幅。
事件时间与定点信号使用整数和规定的 ties-to-even 舍入，氧合使用版本化的浮点模型；
同版本结果不应因 CPU、分块大小或恢复位置而改变。
允许未来版本调整模型，不意味着可以因运行机器不同而改变同一场景。

| 原有开销 | 当前算法 | 边界 |
| --- | --- | --- |
| 每次定点舍入分别求商、余数 | 固定位移使用 `RoundShiftTiesToEven`；其余用二次幂检测或 `UInt128.DivRem` | 保留符号、极值、舍入、除零和溢出语义 |
| 每次读取重新扫描光学测量窗口 | 有界 Int64 统计后再转 BigInteger，按不可变窗口缓存读数 | 保留无效输入验证、500 ms 过期边界和 checkpoint 行为 |
| 十二导联历史每次扫描全部样本并分配伪迹谓词 | 二分定位起点、到终点即停止、倒序索引寻找最后覆盖伪迹 | 保留半开区间和重叠伪迹覆盖顺序 |
| 压力／Pleth 每点重新计算全部尾波 | 固定源时间网格递推，每秒重新锚定，缓存四个不可变秒块 | 新数值模型，随机读、分块、接续及恢复使用同一路径 |
| 氧合库存与流量全部 decimal | double 流量、双 double 库存、同一转移量记账，固定 2 ms RK4 子步 | 保留 8 ms 输运契约，序列化包含低位和模型版本 |
| 每个采样遍历全部事件、形态、相位段 | 按事件和相位段准备参数，批量累加；以整数商余推进相位 | 绝对时间初始化每批；动态呼吸时钟与心源衰减保留精确求值 |
| 延续段每个样本重建事件窗口 | 每批重建共享窗口，超出事件上限时递归拆分 | 不把额外数值状态写入 checkpoint |
| CRC 每字节循环八个位 | 小端 `ulong` 分块，调用 `BitOperations.Crc32C`，处理剩余字节 | 保留初值、补码及线上布局；运行时提供软件退化 |
| PCM 每样本进行 128 位定点除法 | `PCM16 × GainQ15 × 4` 精确整数化简，批量转 float32 | 合法增益内乘积适合 Int32；取消淡出仍按整数舍入 |
| 音频逐点叠加、限幅 | SSE2／AVX2／AVX-512F／AdvSimd 独立内核 | 保留 voice 叠加顺序、NaN 和有符号零行为，尾部标量处理 |
| 合成音每点重新乘算相位 | 从绝对帧初始化后做模加法推进 | 分块与恢复不累计相位漂移 |

压力模型为 `VascularPressureRcIllustration@2`，Pleth 为 `PlethSmoothRunoff@2`，
氧合为 `OxygenTransportDoubleDouble@3`。版本变化承认累计舍入和积分步长的变化；
不把新算法冒充旧模型。缓存只是可重建结果，不携带决定数值的调用历史。
UI 绘制与设备 I/O 未纳入数据路径基准。

## 检测与分派

[y-cruncher 的分派说明](https://www.numberworld.org/y-cruncher/internals/arch-optimizations.html)
区分指令可执行性与调优选择；其作者的
[FeatureDetector](https://github.com/Mysticial/FeatureDetector) 同时检查 CPU 和操作系统支持。
本项目借鉴这一分层方法，没有引入其源码或处理器型号表。

`AudioKernelPolicy.X64.cs` 用 CPUID 读取厂商、family、model，仅供诊断。
执行权限由 .NET 硬件 intrinsic 的 `IsSupported` 决定，包含 CPU、OS 扩展寄存器状态及运行时禁用开关；
不能单凭 CPUID 的 AVX 位执行向量代码。
参见 [.NET intrinsic 支持检测](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.intrinsics.x86.avx.issupported?view=net-10.0)。

按实际内核所需能力依次选择 AVX-512F、AVX2、SSE2，再退回标量。
PCM 使用 AVX-512F 的符号扩展与 Int32 乘法，无额外 AVX-512BW 要求。
ARM64 独立检测 `AdvSimd.IsSupported`，不可用则标量。
向量宽度不改变计算结果；短块按样本数使用完整的较窄向量或直接使用标量，尾部使用标量。
不会根据 N305、树莓派或其他具体处理器型号改变算法。
选择更宽的可用内核是通用策略，不是对所有机器、负载和频率状态的最快保证。

CRC32C 与宽整数乘法由 .NET 按运行环境生成指令；它们不需要额外的 CPU 型号判断。
定点信号路径没有引入浮点运算；氧合的 double 算术保留显式求值顺序，
不使用硬件专属 FMA、快速数学或近似倒数。跨架构验证包含完整库存高、低位。

## 编译与发布隔离

[`Directory.Build.targets`](../../../Directory.Build.targets) 在源文件编译阶段排除另一架构的 `*.X64.cs`／`*.Arm64.cs`。
这与只在同一程序集内用运行时条件跳过代码不同：x64 产品程序集没有 ARM intrinsic 引用，ARM64 产品反之。
.NET 自带的运行时和标准库仍由其自己的架构发布机制管理。

选择顺序是 `RuntimeIdentifier` → 显式 `PlatformTarget` → SDK 主机架构。
因此交叉发布的目标 RID 覆盖构建机架构；同一 x64 包在启动时按目标机器的能力选内核，无需分发 v3／v4 专用包。
使用既有 `tools/build.py`、`tools/build_release.py` 或 `tools/publish_desktop_cross.py` 流程即可。
无 RID 的开发构建选择 SDK 架构，复制开发输出到另一架构前应重新构建。

`-p:MonitorKernelArchitecture=scalar` 可编译仅含标量内核的可移植 IL，供诊断与差分验证。
非标量显式选择必须与目标架构一致；不支持的架构使用标量。

## 验证与性能工具

准备 README 所要求的依赖后：

```sh
python3 tools/test_cpu_architecture.py
dotnet run --project tests/Monitor.Specs --configuration Release -- --cpu-kernels
dotnet run --project tools/Monitor.Performance --configuration Release
dotnet run --project tools/Monitor.Performance --configuration Release -- --data-paths
```

规格覆盖独立数值 oracle、事件标量／批量差分、分块恢复、PCM 边界和已编译程序集的架构引用。
使用 .NET 的 `DOTNET_EnableAVX512=0`、`DOTNET_EnableAVX2=0`、`DOTNET_EnableHWIntrinsic=0`
启动独立进程，可验证较低能力路径；它们不会给不支持的硬件授予新能力。

性能工具测量真实生产 API 的三通道生成／采集／封包和音频渲染，输出中位数、分配量及结果指纹。
`--data-paths` 覆盖定点、光学统计与缓存、真实生成器连续推进、氧合、十二导联历史查询，
以及每帧推进加读数的数据路径；`--data-paths --verify-only` 仅生成核对指纹。
连续生成测量包含每秒重建成本；重复 fork 的短区间可能命中缓存，需与连续结果分别解读。
数据路径的 live 用例固定在预备 45 s 后推进相同帧序列，避免快实现跑到不同仿真场景。
模型版本发生变化时，旧、新氧合或波形指纹可能不同；应分别验证每个版本的跨架构、分块与恢复一致性。
默认分层模式先运行预热，留出后台 JIT 编译间隔，再进行校准和测量；单核忙循环本身不能保证晋级完成。
数据表示预热后吞吐，不表示冷启动或第一次音频回调延迟；原始各批耗时应同时检查是否仍有分层波动。
比较必须使用同一机器、运行时、预热与分层编译设置；默认分层编译和
`DOTNET_TieredCompilation=0` 应分别观察。局部耗时改善不能直接换算为整机 CPU 占用或帧率。
原始日志保存在 Git 忽略的 `artifacts/`，不作为模型接口契约。
